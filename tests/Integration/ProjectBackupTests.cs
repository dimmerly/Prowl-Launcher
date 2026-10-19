using System.IO.Compression;

using Xunit;
namespace Prowl.Launcher.Test;

[Collection("Localization")]
[Trait("Category", "Integration")]
public sealed class ProjectBackupTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlBackupTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            FileSystemHelper.DeleteDirectory(_home);
        }
    }

    private (LauncherStore Store, ProjectBackupService Backups, Project Project) CreateProject(string name = "Game", string version = "1.0")
    {
        string path = Path.Combine(_home, "Projects", name);
        Directory.CreateDirectory(Path.Combine(path, "Assets", "Empty"));
        Directory.CreateDirectory(Path.Combine(path, "ProjectSettings"));
        JsonFile.Write(Path.Combine(path, name + ".prowl"), new
        {
            name, version
        });
        File.WriteAllText(Path.Combine(path, "Assets", "Scene.scene"), "original scene");
        File.WriteAllText(Path.Combine(path, "ProjectSettings", "Physics.json"), "{}");
        LauncherStore store = new( Path.Combine(_home, "Home") );
        return (store, new ProjectBackupService(store), store.AddProject(path));
    }

    private static void Write(string root, string relative, string text = "x")
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void BackupsLeaveOutCachesBuildsVersionControlAndGeneratedFiles()
    {
        (_, ProjectBackupService backups, Project project) = CreateProject();
        foreach (string excluded in new[] { "Library/cache/a.bin", "Temp/b", "Logs/c.log", "Builds/win-x64/Game.exe", "Backups/old.zip", ".git/HEAD",
                     "Game.csproj", "Game.slnx", "Assets/Scripts/bin/Debug/x.dll", "Assets/Scripts/obj/y" })
        {
            Write(project.Path, excluded);
        }
        Write(project.Path, "Assets/Library/kept.txt");

        ProjectBackup backup = backups.Create(project, BackupReason.Manual);

        using ZipArchive zip = ZipFile.OpenRead(backup.Archive);
        string[] entries = zip.Entries.Select(e => e.FullName).ToArray();
        Assert.Contains("Game.prowl", entries);
        Assert.Contains("Assets/Scene.scene", entries);
        Assert.Contains("Assets/Empty/", entries);
        Assert.Contains("Assets/Library/kept.txt", entries);
        Assert.Contains("ProjectSettings/Physics.json", entries);
        Assert.DoesNotContain(entries, e => e.StartsWith("Library") || e.StartsWith("Temp") || e.StartsWith("Logs") || e.StartsWith("Builds")
                                            || e.StartsWith("Backups") || e.StartsWith(".git") || e.EndsWith(".csproj")
                                            || e.EndsWith(".slnx") || e.Contains("/bin/") || e.Contains("/obj/"));
        Assert.DoesNotContain(entries, e => e.Contains('\\'));
        Assert.Equal(entries.Count(e => !e.EndsWith('/')), backup.FileCount);
    }

    [Fact]
    public void UnzippingABackupRestoresTheProject()
    {
        (_, ProjectBackupService backups, Project project) = CreateProject();
        ProjectBackup backup = backups.Create(project, BackupReason.PreUpgrade, "1.1");
        Assert.Equal("1.0", backup.ProjectVersion);
        Assert.Equal("1.1", backup.TargetVersion);

        string restored = Path.Combine(_home, "Restored");
        ReleasePackage.ExtractArchive(backup.Archive, restored);

        Assert.Equal("original scene", File.ReadAllText(Path.Combine(restored, "Assets", "Scene.scene")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(restored, "ProjectSettings", "Physics.json")));
        Assert.True(Directory.Exists(Path.Combine(restored, "Assets", "Empty")));
        Assert.Equal("1.0", LauncherStore.ReadProjectVersion(restored));
    }

    [Fact]
    public void RestoringAsACopyLeavesTheOriginalAndAddsTheCopy()
    {
        (LauncherStore store, ProjectBackupService backups, Project project) = CreateProject();
        backups.Create(project, BackupReason.Manual);
        File.WriteAllText(Path.Combine(project.Path, "Assets", "Scene.scene"), "current");
        ProjectBackup backup = Assert.Single(backups.ListAll());

        Project copy = backups.RestoreAsCopy(backup);
        Project second = backups.RestoreAsCopy(backup);

        Assert.Equal("current", File.ReadAllText(Path.Combine(project.Path, "Assets", "Scene.scene")));
        Assert.Equal("original scene", File.ReadAllText(Path.Combine(copy.Path, "Assets", "Scene.scene")));
        Assert.Equal(Path.GetDirectoryName(project.Path), Path.GetDirectoryName(copy.Path));
        Assert.NotEqual(copy.Path, second.Path);
        Assert.Equal(3, store.Settings.Projects.Count);
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(project.Path)!, "*.prowl-restore-*"));
    }

    [Fact]
    public async Task AllBackupsAreListedIncludingRemovedProjectsAndCanBeDeleted()
    {
        (LauncherStore store, ProjectBackupService backups, Project game) = CreateProject();
        (_, _, Project other) = CreateProject("Other");
        backups.Create(game, BackupReason.Manual);
        backups.Create(other, BackupReason.PreUpgrade, "1.1");
        store.Settings.Projects.Remove(other);
        store.Save();

        IReadOnlyList<ProjectBackup> all = backups.ListAll();
        Assert.Equal(["Other", "Game"], all.Select(b => b.ProjectName));
        Assert.All(all, b => Assert.True(File.Exists(b.Archive)));

        await backups.DeleteAsync(all[0]);
        Assert.Equal("Game", Assert.Single(backups.ListAll()).ProjectName);
        Assert.False(File.Exists(all[0].Archive));
    }

    [Fact]
    public void BackupsWithoutAProjectAreNotRestored()
    {
        (_, ProjectBackupService backups, Project project) = CreateProject();
        ProjectBackup backup = backups.Create(project, BackupReason.Manual);
        string archive = backup.Archive;
        File.Delete(archive);
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            zip.CreateEntry("notes.txt");
        }

        Assert.Throws<InvalidDataException>(() => backups.RestoreAsCopy(Assert.Single(backups.ListAll())));
        Assert.Single(Directory.EnumerateDirectories(Path.GetDirectoryName(project.Path)!));
    }

    [Fact]
    public void CancelledBackupsLeaveNothingBehind()
    {
        (LauncherStore store, ProjectBackupService backups, Project project) = CreateProject();
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => backups.Create(project, BackupReason.Manual, token: cancelled.Token));
        Assert.Empty(backups.List(project));
        string folder = Assert.Single(Directory.EnumerateDirectories(store.BackupsPath));
        Assert.DoesNotContain(Directory.EnumerateFiles(folder), f => Path.GetFileName(f) != "backup.lock");
    }

    [Fact]
    public void RecoveryRemovesUnfinishedBackupsOnly()
    {
        (LauncherStore store, ProjectBackupService backups, Project project) = CreateProject();
        backups.Create(project, BackupReason.Manual);
        string folder = Assert.Single(Directory.EnumerateDirectories(store.BackupsPath));
        File.WriteAllText(Path.Combine(folder, "20260101-000000-deadbeef.zip.partial"), "");
        File.WriteAllText(Path.Combine(folder, "20260101-000000-cafebabe.zip"), "");

        backups.Recover();

        Assert.False(File.Exists(Path.Combine(folder, "20260101-000000-deadbeef.zip.partial")));
        Assert.False(File.Exists(Path.Combine(folder, "20260101-000000-cafebabe.zip")));
        Assert.Single(backups.List(project));
    }

    [Fact]
    public void PruningKeepsTheNewestAutomaticBackupsAndEveryManualOne()
    {
        (LauncherStore store, ProjectBackupService backups, Project project) = CreateProject();
        store.Settings.BackupRetention = 2;
        ProjectBackup manual = backups.Create(project, BackupReason.Manual);
        ProjectBackup[] automatic = Enumerable.Range(0, 4).Select(_ => backups.Create(project, BackupReason.PreUpgrade)).ToArray();

        IReadOnlyList<ProjectBackup> kept = backups.List(project);
        Assert.Equal(3, kept.Count);
        Assert.Contains(kept, b => b.Id == manual.Id);
        Assert.Equal(automatic.TakeLast(2).Select(b => b.Id).Order(), kept.Where(b => b.Automatic).Select(b => b.Id).Order());
        Assert.Equal(3, Directory.EnumerateFiles(Path.GetDirectoryName(manual.Archive)!, "*.zip").Count());
    }

    [Fact]
    public void BackupsFollowARelocatedProject()
    {
        (LauncherStore store, ProjectBackupService backups, Project project) = CreateProject();
        ProjectBackup backup = backups.Create(project, BackupReason.Manual);

        string moved = Path.Combine(_home, "Elsewhere", "Game");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(project.Path, moved);
        store.RelocateProject(project, moved);

        Project reloaded = Assert.Single(new LauncherStore(store.Home).Settings.Projects);
        Assert.Equal(backup.Id, Assert.Single(backups.List(reloaded)).Id);
    }

    [Fact]
    public async Task OnlyACleanGitCheckoutWithACommitCountsAsARestorePoint()
    {
        (_, _, Project project) = CreateProject();
        Assert.False(await ProjectBackupService.IsCommittedInGitAsync(project.Path));
        if (!Git(project.Path, "init", "-q"))
        {
            // git isn't installed.
            return;
        }
        File.WriteAllText(Path.Combine(project.Path, ".gitignore"), "Library/\n");
        Write(project.Path, "Library/cache.bin");
        Assert.False(await ProjectBackupService.IsCommittedInGitAsync(project.Path));

        Assert.True(Git(project.Path, "add", "-A") && Git(project.Path, "commit", "-q", "-m", "Initial"));
        Assert.True(await ProjectBackupService.IsCommittedInGitAsync(project.Path));

        File.WriteAllText(Path.Combine(project.Path, "Assets", "Scene.scene"), "uncommitted");
        Assert.False(await ProjectBackupService.IsCommittedInGitAsync(project.Path));
        Assert.True(Git(project.Path, "checkout", "-q", "--", "."));
        Write(project.Path, "Assets/Untracked.txt");
        Assert.False(await ProjectBackupService.IsCommittedInGitAsync(project.Path));

        static bool Git(string directory, params string[] arguments)
        {
            try
            {
                using System.Diagnostics.Process process = System.Diagnostics.Process.Start(ProcessHelper.Captured("git", directory,
                    ["-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", .. arguments]))!;
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }

    [Fact]
    public void LinksInsideTheProjectAreSkippedNotFollowed()
    {
        (_, ProjectBackupService backups, Project project) = CreateProject();
        string outside = Path.Combine(_home, "Outside");
        Write(outside, "secret.txt");
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(project.Path, "Assets", "Linked"), outside);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Creating links needs Developer Mode or elevation on Windows.
            return;
        }

        ProjectBackup backup = backups.Create(project, BackupReason.Manual);

        using ZipArchive zip = ZipFile.OpenRead(backup.Archive);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("Linked") || e.FullName.Contains("secret"));
    }

    [Fact]
    public void DeletingADirectoryRemovesReadOnlyFiles()
    {
        string folder = Path.Combine(_home, "ReadOnly");
        Write(folder, ".git/objects/ab/cdef", "object");
        File.SetAttributes(Path.Combine(folder, ".git", "objects", "ab", "cdef"), FileAttributes.ReadOnly);

        FileSystemHelper.DeleteDirectory(folder);

        Assert.False(Directory.Exists(folder));
    }
}
