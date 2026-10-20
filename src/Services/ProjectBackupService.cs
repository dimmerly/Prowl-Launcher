using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Enumeration;

using Prowl.Rosetta;

namespace Prowl.Launcher;

/// <summary>
///     Zip backups of projects in the launcher home, one folder per project. A backup's manifest is written only
///     after its zip is complete, so an interrupted backup is never mistaken for a finished one. Backups are
///     restored as a copy next to the project, so restoring never touches the original.
/// </summary>
internal sealed class ProjectBackupService(LauncherStore store)
{
    private const string LockFile = "backup.lock";
    private const string PartialSuffix = ".partial";
    private const string StagingMarker = ".prowl-restore-";
    private static readonly DateTime ZipEpoch = new(1980, 1, 2, 0, 0, 0, DateTimeKind.Local);
    private static readonly DateTime ZipLimit = new(2107, 12, 30, 0, 0, 0, DateTimeKind.Local);

    /// <summary>The project's backups, newest first. Incomplete or orphaned files are ignored.</summary>
    public IReadOnlyList<ProjectBackup> List(Project project) =>
        FolderFor(project) is { } folder && Directory.Exists(folder)
            ? Completed(folder).OrderByDescending(backup => backup.CreatedAt).ToArray()
            : [];

    /// <summary>Every project's backups, newest first, including backups of projects no longer in the list.</summary>
    public IReadOnlyList<ProjectBackup> ListAll()
    {
        if (!Directory.Exists(store.BackupsPath) || FileSystemHelper.IsLink(store.BackupsPath))
        {
            return [];
        }

        return Directory.EnumerateDirectories(store.BackupsPath)
            .Where(folder =>
                Guid.TryParseExact(Path.GetFileName(folder), "N", out _) && !FileSystemHelper.IsLink(folder))
            .SelectMany(Completed)
            .OrderByDescending(backup => backup.CreatedAt)
            .ToArray();
    }

    public Task DeleteAsync(ProjectBackup backup) => Task.Run(() =>
    {
        string folder = BackupFolder(backup);
        using FileStream backupLock = AcquireLock(folder);
        // The manifest goes first, so an interrupted delete leaves an orphaned zip that Recover removes.
        DeleteFile(FileSystemHelper.SafeChildPath(folder, backup.Id + ".json"));
        DeleteFile(FileSystemHelper.SafeChildPath(folder, backup.Id + ".zip"));
    });

    /// <summary>Extracts in the background, then adds the copy to the project list on the calling (UI) thread.</summary>
    public async Task<Project> RestoreAsCopyAsync(ProjectBackup backup, IProgress<TransferProgress>? progress,
        CancellationToken token)
    {
        string copy = await Task.Run(() => ExtractCopy(backup, progress, token), token);
        return store.AddProject(copy);
    }

    internal Project RestoreAsCopy(ProjectBackup backup, IProgress<TransferProgress>? progress = null,
        CancellationToken token = default) => store.AddProject(ExtractCopy(backup, progress, token));

    /// <summary>
    ///     Unzips <paramref name="backup" /> into a new folder next to the original project (or in the default
    ///     projects folder when that's gone) and returns that folder. The original is never touched.
    /// </summary>
    private string ExtractCopy(ProjectBackup backup, IProgress<TransferProgress>? progress,
        CancellationToken token)
    {
        string archive = FileSystemHelper.SafeChildPath(BackupFolder(backup), backup.Id + ".zip");
        string? original = Path.IsPathFullyQualified(backup.ProjectPath)
            ? Path.GetDirectoryName(backup.ProjectPath)
            : null;
        string parent = original != null && Directory.Exists(original) ? original : EditorSettings.ProjectsDirectory();
        Directory.CreateDirectory(parent);

        string name =
            $"{Path.GetFileName(backup.ProjectPath.TrimEnd('/', '\\'))} ({Loc.Get("launcher.backups.restored_suffix")} {backup.CreatedAt.ToLocalTime():yyyy-MM-dd HH-mm})";
        string destination = FileSystemHelper.SafeChildPath(parent, name);
        for (int i = 2; Directory.Exists(destination) || File.Exists(destination); i++)
        {
            destination = FileSystemHelper.SafeChildPath(parent, $"{name} {i}");
        }

        string staging = destination + StagingMarker + Guid.NewGuid().ToString("N");
        try
        {
            progress?.Report(new TransferProgress("launcher.backups.extracting"));
            ReleasePackage.ExtractArchive(archive, staging, token);
            if (!Directory.Exists(Path.Combine(staging, "Assets")))
            {
                throw new InvalidDataException(Loc.Get("launcher.errors.backup_damaged"));
            }

            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                FileSystemHelper.DeleteDirectory(staging);
            }
        }

        return destination;
    }

    /// <summary>Assigns the project's backup folder on the calling (UI) thread, then zips in the background.</summary>
    public Task<ProjectBackup> CreateAsync(Project project, BackupReason reason, string? targetVersion,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        AssignBackupId(project);
        return Task.Run(() => Create(project, reason, targetVersion, progress, token), token);
    }

    /// <summary>
    ///     Zips the project, leaving out regenerated caches, builds, version control and filesystem links, then
    ///     prunes older automatic backups.
    /// </summary>
    internal ProjectBackup Create(Project project, BackupReason reason, string? targetVersion = null,
        IProgress<TransferProgress>? progress = null, CancellationToken token = default)
    {
        string root = Path.GetFullPath(project.Path);
        if (!Directory.Exists(Path.Combine(root, "Assets")))
        {
            throw new DirectoryNotFoundException(Loc.Get("launcher.errors.missing_project_assets"));
        }

        string folder = EnsureFolder(project);
        using FileStream backupLock = AcquireLock(folder);

        progress?.Report(new TransferProgress("launcher.backups.scanning"));
        List<(string Path, string Entry, bool IsDirectory)> entries = Collect(root, token);
        long total = entries.Where(e => !e.IsDirectory).Sum(e => new FileInfo(e.Path).Length);
        ReleasePackage.RequireFreeSpace(folder, total);

        string id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
        string archive = Path.Combine(folder, id + ".zip");
        string partial = archive + PartialSuffix;
        try
        {
            int files = WriteArchive(partial, entries, total, progress, token);
            ProjectBackup backup = new()
            {
                Id = id,
                ProjectName = project.Name,
                ProjectPath = root,
                CreatedAt = DateTimeOffset.UtcNow,
                Reason = reason,
                ProjectVersion = LauncherStore.ReadProjectVersion(root),
                TargetVersion = targetVersion,
                FileCount = files,
                UncompressedBytes = total,
                ArchiveBytes = new FileInfo(partial).Length,
                Archive = archive
            };
            File.Move(partial, archive);
            // The manifest commits the backup.
            JsonFile.Write(Path.Combine(folder, id + ".json"), backup);
            Prune(folder);
            return backup;
        }
        catch
        {
            DeleteFile(partial);
            if (!File.Exists(Path.Combine(folder, id + ".json")))
            {
                DeleteFile(archive);
            }

            throw;
        }
    }

    /// <summary>
    ///     Removes backups a previous launcher left unfinished. Folders another launcher is backing up are left
    ///     alone; failures are logged, never thrown.
    /// </summary>
    public void Recover()
    {
        if (!Directory.Exists(store.BackupsPath) || FileSystemHelper.IsLink(store.BackupsPath))
        {
            return;
        }

        foreach (string folder in Directory.EnumerateDirectories(store.BackupsPath))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _) || FileSystemHelper.IsLink(folder))
            {
                continue;
            }

            try
            {
                using FileStream backupLock = AcquireLock(folder);
                foreach (string file in Directory.EnumerateFiles(folder))
                {
                    string name = Path.GetFileName(file);
                    bool unfinished = name.EndsWith(PartialSuffix, StringComparison.Ordinal)
                                      || name.EndsWith(".tmp", StringComparison.Ordinal)
                                      || (name.EndsWith(".zip", StringComparison.Ordinal)
                                          && !File.Exists(Path.ChangeExtension(file, ".json")));
                    if (unfinished)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LauncherLog.Append(store.Home, error);
            }
        }
    }

    /// <summary>
    ///     Whether the project is in a git repository with at least one commit and no uncommitted or untracked
    ///     changes, so its last commit already is a restore point. False whenever git is missing, slow or fails.
    /// </summary>
    public static async Task<bool> IsCommittedInGitAsync(string root, CancellationToken token = default)
    {
        return await GitAsync("rev-parse", "--verify", "--quiet", "HEAD") is (0, _)
               && await GitAsync("status", "--porcelain", "--untracked-files=all", "--", ".") is (0, var changes)
               && string.IsNullOrWhiteSpace(changes);

        async Task<(int ExitCode, string Output)?> GitAsync(params string[] arguments)
        {
            try
            {
                using Process process = Process.Start(ProcessHelper.Captured("git", null, ["-C", root, .. arguments]))!;
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(Constants.Backups.GitCheckTimeout);
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
                return (process.ExitCode, output);
            }
            catch (Exception e) when (e is Win32Exception or IOException
                                      || (e is OperationCanceledException && !token.IsCancellationRequested))
            {
                return null;
            }
        }
    }

    /// <summary>The project's files and folders in a stable order, with forward-slash entry names.</summary>
    private List<(string Path, string Entry, bool IsDirectory)> Collect(string root, CancellationToken token)
    {
        List<(string, string, bool)> entries = [];
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            token.ThrowIfCancellationRequested();
            bool atRoot = directory == root;
            foreach (FileSystemInfo item in new DirectoryInfo(directory).EnumerateFileSystemInfos()
                         .OrderBy(i => i.Name, StringComparer.Ordinal))
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // Following a link could back up files outside the project.
                    LauncherLog.Append(store.Home, $"Backup skipped the link {item.FullName}.");
                    continue;
                }

                string entry = Path.GetRelativePath(root, item.FullName);
                if (Path.DirectorySeparatorChar != '\\' && entry.Contains('\\'))
                {
                    LauncherLog.Append(store.Home,
                        $"Backup skipped {item.FullName}: its name can't be stored portably.");
                    continue;
                }

                entry = entry.Replace(Path.DirectorySeparatorChar, '/');
                if (item is DirectoryInfo)
                {
                    if (IsExcludedFolder(item.Name, atRoot))
                    {
                        continue;
                    }

                    entries.Add((item.FullName, entry + "/", true));
                    pending.Push(item.FullName);
                }
                else if (!Constants.Backups.ExcludedFiles.Any(pattern =>
                             FileSystemName.MatchesSimpleExpression(pattern, item.Name)))
                {
                    entries.Add((item.FullName, entry, false));
                }
            }
        }

        return entries;
    }

    private static bool IsExcludedFolder(string name, bool atRoot) =>
        Constants.Backups.ExcludedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)
        || (atRoot && Constants.Backups.ExcludedRootFolders.Contains(name, StringComparer.OrdinalIgnoreCase));

    /// <summary>Writes the archive, keeping Unix modes so unzipped scripts stay executable. Returns the file count.</summary>
    private static int WriteArchive(string path, List<(string Path, string Entry, bool IsDirectory)> entries,
        long total,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        const int UnixRegularFile = 0x8000, UnixDirectory = 0x4000;

        int files = 0;
        long written = 0, lastReport = 0;
        Stopwatch clock = Stopwatch.StartNew();
        using FileStream output = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            Constants.Network.TransferBufferSize);
        using ZipArchive zip = new(output, ZipArchiveMode.Create);
        foreach ((string source, string name, bool isDirectory) in entries)
        {
            token.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = zip.CreateEntry(name,
                isDirectory ? CompressionLevel.NoCompression : CompressionLevel.Fastest);
            DateTime modified = isDirectory ? Directory.GetLastWriteTime(source) : File.GetLastWriteTime(source);
            entry.LastWriteTime = modified < ZipEpoch ? ZipEpoch : modified > ZipLimit ? ZipLimit : modified;
            if (!OperatingSystem.IsWindows())
            {
                entry.ExternalAttributes =
                    ((isDirectory ? UnixDirectory : UnixRegularFile) | (int)File.GetUnixFileMode(source)) << 16;
            }

            if (isDirectory)
            {
                continue;
            }

            // The editor may still have files open; share rather than fail, but never skip a file silently.
            using (FileStream input = new(source, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (Stream stream = entry.Open())
            {
                input.CopyTo(stream);
                written += input.Length;
            }

            files++;
            if (clock.ElapsedMilliseconds - lastReport > Constants.Network.ProgressIntervalMilliseconds)
            {
                progress?.Report(new TransferProgress(
                    Loc.Get("launcher.backups.progress",
                        new { count = files, total = entries.Count(e => !e.IsDirectory) }),
                    total > 0 ? Math.Min(1, (double)written / total) : null));
                lastReport = clock.ElapsedMilliseconds;
            }
        }

        return files;
    }

    /// <summary>Deletes the oldest automatic backups beyond the retention setting. Manual backups are kept.</summary>
    private void Prune(string folder)
    {
        int retention = Math.Max(1, store.Settings.BackupRetention);
        IEnumerable<ProjectBackup> expired = Manifests(folder)
            .Where(backup => backup.Automatic)
            .OrderByDescending(backup => backup.CreatedAt)
            .Skip(retention);
        foreach (ProjectBackup backup in expired.ToArray())
        {
            // The manifest goes first, so an interrupted delete leaves an orphaned zip that Recover removes.
            DeleteFile(FileSystemHelper.SafeChildPath(folder, backup.Id + ".json"));
            DeleteFile(FileSystemHelper.SafeChildPath(folder, backup.Id + ".zip"));
        }
    }

    private static IEnumerable<ProjectBackup> Manifests(string folder) => Directory.EnumerateFiles(folder, "*.json")
        .Select(manifest => (Path: manifest, Backup: JsonFile.Read<ProjectBackup>(manifest)))
        .Where(item => item.Backup != null && item.Backup.Id == Path.GetFileNameWithoutExtension(item.Path))
        .Select(item =>
        {
            item.Backup!.Archive = Path.ChangeExtension(item.Path, ".zip");
            return item.Backup;
        });

    private static IEnumerable<ProjectBackup> Completed(string folder) =>
        Manifests(folder).Where(backup => File.Exists(backup.Archive));

    /// <summary>The folder holding a listed backup, which must be one of the launcher's backup folders.</summary>
    private string BackupFolder(ProjectBackup backup)
    {
        string? folder = Path.GetDirectoryName(backup.Archive);
        if (folder == null || !FileSystemHelper.PathsEqual(Path.GetDirectoryName(folder) ?? "", store.BackupsPath))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.path_outside_folder"));
        }

        return folder;
    }

    private string? FolderFor(Project project) => project.BackupId is { } id && Guid.TryParseExact(id, "N", out _)
        ? FileSystemHelper.SafeChildPath(store.BackupsPath, id)
        : null;

    /// <summary>Gives the project a backup folder id. This saves settings, so it belongs on the UI thread.</summary>
    private void AssignBackupId(Project project)
    {
        if (FolderFor(project) == null)
        {
            project.BackupId = Guid.NewGuid().ToString("N");
            store.Save();
        }
    }

    private string EnsureFolder(Project project)
    {
        AssignBackupId(project);
        Directory.CreateDirectory(store.BackupsPath);
        FileSystemHelper.RejectLink(store.BackupsPath);
        string folder = FolderFor(project)!;
        Directory.CreateDirectory(folder);
        FileSystemHelper.RejectLink(folder);
        return folder;
    }

    /// <summary>Serializes backups of one project across launcher processes.</summary>
    private static FileStream AcquireLock(string folder)
    {
        try
        {
            return new FileStream(Path.Combine(folder, LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException error)
        {
            throw new IOException(Loc.Get("launcher.errors.backup_in_progress"), error);
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
