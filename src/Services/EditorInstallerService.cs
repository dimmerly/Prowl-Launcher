using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

using Prowl.Rosetta;

namespace Prowl.Launcher;

/// <summary>
///     Installs, repairs, uninstalls and launches editors. Every change goes through a journaled workspace so
///     an interrupted install never loses the previous installation.
/// </summary>
public sealed partial class EditorInstallerService(HttpClient http, LauncherStore store)
{
    private const string EditorName = "Prowl.Editor";
    private const string RepairJournal = "repair.json";

    public async Task<InstalledEditor> InstallAsync(
        EditorRelease release,
        string platform,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default,
        string? sourceRepository = null
    )
    {
        ReleaseAsset asset = release.AssetFor(platform)
                             ?? throw new InvalidOperationException(Loc.Get("launcher.errors.no_platform_build",
                                 new { platform }));
        if (release.Id <= 0 || !Constants.Storage.SupportedPlatforms.Contains(platform))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_release"));
        }

        using FileStream operation = BeginOperation();
        string repository = GitHubRepository.Normalize(sourceRepository ?? store.Settings.ProwlRepository);
        ReleasePackage.RequireFromRepository(asset, repository);
        InstalledEditor editor =
            new(release.Id, release.Tag, platform, "", DateTimeOffset.UtcNow) { Repository = repository };
        string destination = store.InstallPath(editor);
        if (Directory.Exists(destination))
        {
            RejectLink(destination);
            ThrowIfRunning(editor);
        }

        using InstallWorkspace workspace = new(store);
        string archive = Path.Combine(workspace.Work, "editor.zip");
        await ReleasePackage.DownloadVerifiedAsync(http, asset, archive, progress, token, store.DownloadsPath);
        progress?.Report(new TransferProgress("launcher.download.extracting"));
        await Task.Run(() => ReleasePackage.ExtractArchive(archive, workspace.Staging, token), token);

        string executable = Directory
            .EnumerateFiles(workspace.Staging, Platform.ExecutableName(EditorName, platform),
                SearchOption.AllDirectories)
            .FirstOrDefault() ?? throw new InvalidDataException(Loc.Get("launcher.errors.missing_editor_archive"));
        ReleasePackage.MarkExecutable(executable);
        editor = editor with { ExecutableRelativePath = Path.GetRelativePath(workspace.Staging, executable) };

        CommitInstall(editor, workspace, destination, token);
        progress?.Report(new TransferProgress(Loc.Get("launcher.download.installed", new { version = release.Tag }),
            1));
        return editor;
    }

    /// <summary>Deletes the installation in the background, then updates settings on the calling (UI) thread.</summary>
    public async Task UninstallAsync(InstalledEditor editor, CancellationToken token)
    {
        if (!await Task.Run(() => DeleteInstallation(editor), token))
        {
            return;
        }

        // Projects keep their pinned editor so reinstalling this version restores the association.
        if (store.Settings.DefaultEditorKey == editor.Key)
        {
            store.Settings.DefaultEditorKey = null;
        }

        store.Save();
    }

    /// <summary>Returns false when the installation was already gone.</summary>
    private bool DeleteInstallation(InstalledEditor editor)
    {
        using FileStream operation = store.AcquireOperationLock();
        RecoverInterruptedInstalls();
        string path = store.InstallPath(editor);
        if (!Directory.Exists(path))
        {
            return false;
        }

        if (IsRunning(editor))
        {
            throw new InvalidOperationException(Loc.Get("launcher.errors.close_before_uninstall"));
        }

        RejectLink(store.Home);
        RejectLink(store.VersionsPath);
        RejectLink(path);
        FileSystemHelper.DeleteDirectory(path);
        return true;
    }

    public void RecoverInterruptedOperations()
    {
        using FileStream operation = store.AcquireOperationLock();
        RecoverInterruptedInstalls();
    }

    /// <summary>
    ///     Restores previous installations left behind by an interrupted <see cref="CommitInstall" />.
    ///     Folders that cannot be recovered are retained and logged, never deleted.
    /// </summary>
    internal void RecoverInterruptedInstalls(Action<string, string>? move = null)
    {
        if (!Directory.Exists(store.WorkPath))
        {
            return;
        }

        RejectLink(store.Home);
        RejectLink(store.WorkPath);
        if (Directory.Exists(store.VersionsPath))
        {
            RejectLink(store.VersionsPath);
        }

        foreach (string work in Directory.EnumerateDirectories(store.WorkPath))
        {
            // Only journaled installer work is recoverable; running samples own other folders.
            if (!Guid.TryParseExact(Path.GetFileName(work), "N", out _))
            {
                continue;
            }

            RejectLink(work);
            InstalledEditor? editor = JsonFile.Read<InstalledEditor>(Path.Combine(work, RepairJournal));
            if (editor == null)
            {
                continue;
            }

            try
            {
                RestoreBackup(editor, Path.Combine(work, InstallWorkspace.BackupName), move ?? Directory.Move);
                Directory.Delete(work, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                LauncherLog.Append(store.Home, $"Repair recovery retained {work}: {error}");
            }
        }
    }

    private void RestoreBackup(InstalledEditor editor, string backup, Action<string, string> move)
    {
        if (!Directory.Exists(backup))
        {
            return;
        }

        RejectLink(backup);
        string destination = store.InstallPath(editor);
        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(store.VersionsPath);
            move(backup, destination);
            return;
        }

        // The swap completed; only discard the backup if the new installation is intact.
        RejectLink(destination);
        InstalledEditor? installed = JsonFile.Read<InstalledEditor>(Path.Combine(destination, "installation.json"));
        if (installed != editor || !File.Exists(store.ExecutablePath(installed)))
        {
            throw new IOException(
                "The interrupted editor repair has an incomplete destination; its backup was retained.");
        }

        Directory.Delete(backup, true);
    }

    public bool IsRunning(InstalledEditor editor) =>
        ProcessHelper.IsRunningUnder(EditorName, store.InstallPath(editor));

    public ProcessStartInfo LaunchInfo(InstalledEditor editor, string? project = null)
    {
        string executable = store.ExecutablePath(editor);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(Loc.Get("launcher.errors.missing_editor_installation"));
        }

        ProcessStartInfo info = new(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false
        };
        if (project != null)
        {
            if (!Directory.Exists(Path.Combine(project, "Assets")))
            {
                throw new DirectoryNotFoundException(Loc.Get("launcher.errors.missing_project_assets"));
            }

            info.ArgumentList.Add("--project");
            info.ArgumentList.Add(Path.GetFullPath(project));
        }

        return info;
    }

    /// <summary>The .NET SDK major version the editor targets, read from its runtimeconfig.</summary>
    public int RequiredSdkMajor(InstalledEditor editor)
    {
        string path = Path.Combine(Path.GetDirectoryName(store.ExecutablePath(editor))!,
            EditorName + ".runtimeconfig.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        string tfm = document.RootElement.GetProperty("runtimeOptions").GetProperty("tfm").GetString() ?? "net10.0";
        return int.TryParse(tfm.Replace("net", "").Split('.')[0], out int major) ? major : 10;
    }

    public static async Task<bool> HasSdkAsync(int major, CancellationToken token = default)
    {
        try
        {
            using Process process = Process.Start(ProcessHelper.Captured("dotnet", null, "--list-sdks"))!;
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(Constants.Startup.SdkCheckTimeout);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                throw;
            }

            string output = await stdout;
            await stderr;
            return output.Split('\n').Any(line => line.TrimStart().StartsWith(major + ".", StringComparison.Ordinal));
        }
        catch (Exception e) when (e is Win32Exception or IOException
                                  || (e is OperationCanceledException && !token.IsCancellationRequested))
        {
            return false;
        }
    }

    /// <summary>Validates the launcher folders, takes the operation lock and recovers earlier interrupted work.</summary>
    private FileStream BeginOperation(params string[] extraFolders)
    {
        Directory.CreateDirectory(store.WorkPath);
        Directory.CreateDirectory(store.VersionsPath);
        foreach (string folder in extraFolders)
        {
            Directory.CreateDirectory(folder);
        }

        foreach (string folder in new[] { store.Home, store.WorkPath, store.VersionsPath }.Concat(extraFolders))
        {
            RejectLink(folder);
        }

        FileStream operation = store.AcquireOperationLock();
        try
        {
            RecoverInterruptedInstalls();
            return operation;
        }
        catch
        {
            operation.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Moves the staged editor into place. The repair journal lets <see cref="RecoverInterruptedInstalls" />
    ///     restore the previous installation if the swap is interrupted.
    /// </summary>
    private void CommitInstall(InstalledEditor editor, InstallWorkspace workspace, string destination,
        CancellationToken token)
    {
        JsonFile.Write(Path.Combine(workspace.Staging, "installation.json"), editor);
        token.ThrowIfCancellationRequested();
        ThrowIfRunning(editor);

        JsonFile.Write(Path.Combine(workspace.Work, RepairJournal), editor);
        DirectoryReplacement.Swap(workspace.Staging, destination, workspace.Backup);
        if (store.Settings.DefaultEditorKey == null)
        {
            store.Settings.DefaultEditorKey = editor.Key;
            store.Save();
        }
    }

    private void ThrowIfRunning(InstalledEditor editor)
    {
        if (IsRunning(editor))
        {
            throw new InvalidOperationException(Loc.Get("launcher.errors.close_before_repair"));
        }
    }

    private static void RejectLink(string path)
    {
        if (FileSystemHelper.IsLink(path))
        {
            throw new IOException(Loc.Get("launcher.errors.installation_link"));
        }
    }

    /// <summary>A generated folder under Work holding the download, the staged editor and the swap backup.</summary>
    private sealed class InstallWorkspace : IDisposable
    {
        internal const string BackupName = "previous";

        public InstallWorkspace(LauncherStore store)
        {
            Work = Path.Combine(store.WorkPath, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Work);
        }

        public string Work
        {
            get;
        }

        public string Staging => Path.Combine(Work, "extracted");
        public string Backup => Path.Combine(Work, BackupName);

        // Keep the journal and old installation if rollback or backup cleanup failed.
        public void Dispose()
        {
            if (Directory.Exists(Work) && !Directory.Exists(Backup))
            {
                Directory.Delete(Work, true);
            }
        }
    }
}
