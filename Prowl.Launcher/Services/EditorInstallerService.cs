using Prowl.Rosetta;

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Prowl.Launcher;

public sealed class EditorInstallerService(HttpClient http, LauncherStore store)
{
    public async Task<InstalledEditor> InstallAsync(
        EditorRelease release,
        string platform,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default,
        string? sourceRepository = null
    )
    {
        ReleaseAsset asset = release.AssetFor(platform) ?? throw new InvalidOperationException(Loc.Get("launcher.errors.no_platform_build", new
        {
            platform
        }));
        if (release.Id <= 0 || !LauncherStore.SupportedPlatforms.Contains(platform))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_release"));
        }

        Directory.CreateDirectory(store.WorkPath);
        Directory.CreateDirectory(store.VersionsPath);
        RejectLinks(store.Home);
        RejectLinks(store.WorkPath);
        RejectLinks(store.VersionsPath);
        using FileStream operation = AcquireOperationLock();
        RecoverInterruptedInstalls();
        string repository = GitHubRepositoryHelper.Normalize(sourceRepository ?? store.Settings.ProwlRepository);
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out Uri? source)
            || source.Scheme != "https" || source.Host != "github.com"
            || !source.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_download_source"));
        InstalledEditor editor = new( release.Id, release.Tag, platform, "", DateTimeOffset.UtcNow ) { Repository = repository };
        string destination = store.InstallPath(editor);
        if (Directory.Exists(destination))
        {
            RejectLinks(destination);
            if (IsRunning(editor))
            {
                throw new InvalidOperationException(Loc.Get("launcher.errors.close_before_repair"));
            }
        }

        string work = Path.Combine(store.WorkPath, Guid.NewGuid().ToString("N"));
        string staging = Path.Combine(work, "extracted");
        string backup = Path.Combine(work, "previous");
        Directory.CreateDirectory(work);
        try
        {
            string archive = Path.Combine(work, "editor.zip");
            await PackageDownloadService.DownloadVerifiedAsync(http, asset, archive, progress, token);
            progress?.Report(new TransferProgress("launcher.download.extracting"));
            await Task.Run(() => ExtractArchive(archive, staging, token), token);
            string executableName = platform.StartsWith("win-", StringComparison.Ordinal) ? "Prowl.Editor.exe" : "Prowl.Editor";
            string? executable = Directory.EnumerateFiles(staging, executableName, SearchOption.AllDirectories).FirstOrDefault();
            if (executable == null)
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.missing_editor_archive"));
            }

            editor = editor with
            {
                ExecutableRelativePath = Path.GetRelativePath(staging, executable)
            };
            LauncherStore.WriteJson(Path.Combine(staging, "installation.json"), editor);
            token.ThrowIfCancellationRequested();
            if (IsRunning(editor))
            {
                throw new InvalidOperationException(Loc.Get("launcher.errors.close_before_repair"));
            }

            LauncherStore.WriteJson(Path.Combine(work, "repair.json"), editor);
            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backup);
            }

            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (Directory.Exists(backup))
                {
                    Directory.Move(backup, destination);
                }

                throw;
            }

            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            if (store.Settings.DefaultEditorKey == null)
            {
                store.Settings.DefaultEditorKey = editor.Key;
                store.Save();
            }

            progress?.Report(new TransferProgress(Loc.Get("launcher.download.installed", new
            {
                version = release.Tag
            }), 1));
            return editor;
        }
        finally
        {
            // This is a generated child of Work, never a release-supplied path.
            // Keep the journal and old installation if rollback or backup cleanup failed.
            if (Directory.Exists(work) && !Directory.Exists(backup))
            {
                Directory.Delete(work, true);
            }
        }
    }

    public void RecoverInterruptedOperations()
    {
        using FileStream operation = AcquireOperationLock();
        RecoverInterruptedInstalls();
    }

    internal void RecoverInterruptedInstalls(Action<string, string>? move = null)
    {
        if (!Directory.Exists(store.WorkPath)) return;
        RejectLinks(store.Home);
        RejectLinks(store.WorkPath);
        if (Directory.Exists(store.VersionsPath)) RejectLinks(store.VersionsPath);
        foreach (string work in Directory.EnumerateDirectories(store.WorkPath))
        {
            // Only journaled installer work is recoverable; running samples own other folders.
            if (!Guid.TryParseExact(Path.GetFileName(work), "N", out _)) continue;
            RejectLinks(work);
            InstalledEditor? editor = LauncherStore.ReadJson<InstalledEditor>(Path.Combine(work, "repair.json"));
            if (editor == null) continue;
            string backup = Path.Combine(work, "previous");
            try
            {
                string destination = store.InstallPath(editor);
                if (Directory.Exists(backup))
                {
                    RejectLinks(backup);
                    if (!Directory.Exists(destination))
                    {
                        Directory.CreateDirectory(store.VersionsPath);
                        (move ?? Directory.Move)(backup, destination);
                    }
                    else
                    {
                        RejectLinks(destination);
                        InstalledEditor? installed = LauncherStore.ReadJson<InstalledEditor>(Path.Combine(destination, "installation.json"));
                        if (installed != editor || !File.Exists(store.ExecutablePath(installed)))
                            throw new IOException("The interrupted editor repair has an incomplete destination; its backup was retained.");
                        Directory.Delete(backup, true);
                    }
                }
                Directory.Delete(work, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Never discard a previous installation when recovery cannot finish.
                try { File.AppendAllText(Path.Combine(store.Home, "launcher.log"), $"{DateTimeOffset.UtcNow:o} Repair recovery retained {work}: {error}\n"); }
                catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    public static void ExtractArchive(string archive, string staging, CancellationToken token = default)
    {
        Directory.CreateDirectory(staging);
        using ZipArchive zip = ZipFile.OpenRead(archive);
        long expanded = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            string relative = entry.FullName.Replace('\\', '/');
            string path = LauncherStore.SafeChildPath(staging, relative);
            int unixMode = entry.ExternalAttributes >> 16 & 0xffff;
            int fileType = unixMode & 0xf000;
            if (fileType != 0 && fileType != 0x8000 && fileType != 0x4000)
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.unsupported_archive_entry"));
            }

            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.archive_link"));
            }

            expanded = checked(expanded + entry.Length);
            if (expanded > 8L * 1024 * 1024 * 1024)
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.archive_too_large"));
            }

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (Stream input = entry.Open())
            {
                using (FileStream output = new( path, FileMode.CreateNew ))
                {
                    input.CopyTo(output);
                }
            }

            if (!OperatingSystem.IsWindows() && unixMode != 0)
            {
                File.SetUnixFileMode(path, (UnixFileMode)(unixMode & 0x1ff));
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            foreach (string executable in Directory.EnumerateFiles(staging, "Prowl.Editor", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
            }
        }
    }

    public void Uninstall(InstalledEditor editor)
    {
        using FileStream operation = AcquireOperationLock();
        RecoverInterruptedInstalls();
        string path = store.InstallPath(editor);
        if (!Directory.Exists(path))
        {
            return;
        }

        if (IsRunning(editor))
        {
            throw new InvalidOperationException(Loc.Get("launcher.errors.close_before_uninstall"));
        }

        RejectLinks(store.Home);
        RejectLinks(store.VersionsPath);
        RejectLinks(path);
        Directory.Delete(path, true);
        if (store.Settings.DefaultEditorKey == editor.Key)
        {
            store.Settings.DefaultEditorKey = null;
        } // Retain project pins so reinstalling this version restores their association.

        store.Save();
    }

    private FileStream AcquireOperationLock()
    {
        try
        {
            return new FileStream(
                Path.Combine(store.Home, "operations.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            );
        }
        catch (IOException)
        {
            throw new IOException(Loc.Get("launcher.errors.operation_in_progress"));
        }
    }

    private static void RejectLinks(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(Loc.Get("launcher.errors.installation_link"));
        }
    }

    public bool IsRunning(InstalledEditor editor)
    {
        string root = store.InstallPath(editor) + Path.DirectorySeparatorChar;
        foreach (Process process in Process.GetProcessesByName("Prowl.Editor"))
        {
            using (process)
            {
                try
                {
                    string? file = process.MainModule?.FileName;
                    if (file != null && file.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public ProcessStartInfo LaunchInfo(InstalledEditor editor, string? project = null)
    {
        string executable = store.ExecutablePath(editor);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(Loc.Get("launcher.errors.missing_editor_installation"));
        }

        ProcessStartInfo info = new( executable )
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

    public int RequiredSdkMajor(InstalledEditor editor)
    {
        string path = Path.Combine(Path.GetDirectoryName(store.ExecutablePath(editor))!, "Prowl.Editor.runtimeconfig.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        string tfm = document.RootElement.GetProperty("runtimeOptions")
            .GetProperty("tfm")
            .GetString() ?? "net10.0";
        return int.TryParse(tfm.Replace("net", "").Split('.')[0], out int major) ? major : 10;
    }

    public static async Task<bool> HasSdkAsync(int major, CancellationToken token = default)
    {
        try
        {
            ProcessStartInfo info = new( "dotnet" )
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            info.ArgumentList.Add("--list-sdks");
            using Process process = Process.Start(info)!;
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            string output = await stdout;
            await stderr;
            return output.Split('\n')
                .Any(line => line.TrimStart().StartsWith(major + ".", StringComparison.Ordinal));
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException || e is OperationCanceledException && !token.IsCancellationRequested)
        {
            return false;
        }
    }
}
