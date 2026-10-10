using System.Diagnostics;

using Prowl.Launcher;
using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class SourceEditorTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlSourceTests", Guid.NewGuid().ToString("N"));
    private readonly List<string[]> _commands = [];
    private bool _failBuild;
    private bool _cancelBuild;
    private bool _dirty;
    private string _commit = new('a', 40);

    private Task<string> RunAsync(ProcessStartInfo info, CancellationToken token)
    {
        string[] arguments = info.ArgumentList.ToArray();
        _commands.Add([info.FileName, .. arguments]);
        if (arguments[0] == "clone")
        {
            Directory.CreateDirectory(arguments[^1]);
        }
        if (arguments[0] == "status")
        {
            return Task.FromResult(_dirty ? " M Prowl.Editor/Program.cs" : "");
        }
        if (arguments[0] == "rev-parse")
        {
            return Task.FromResult(_commit);
        }
        if (info.FileName == "dotnet")
        {
            string staging = arguments[^1];
            Directory.CreateDirectory(staging);
            string executable = OperatingSystem.IsWindows() ? "Prowl.Editor.exe" : "Prowl.Editor";
            File.WriteAllText(Path.Combine(staging, executable), _commit);
            if (_failBuild)
            {
                throw new IOException("Compilation failed.");
            }
            if (_cancelBuild)
            {
                throw new OperationCanceledException(token);
            }
            File.Copy(typeof(Launcher).Assembly.Location, Path.Combine(staging, "Prowl.Editor.dll"));
            File.WriteAllText(Path.Combine(staging, "Prowl.Editor.runtimeconfig.json"), """
                {"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}
                """);
        }
        return Task.FromResult("");
    }

    [Fact]
    public async Task CloneThenPull_InstallsEachCommitAndPreservesProjectSelections()
    {
        LauncherStore store = new(_home);
        using HttpClient http = new();
        EditorInstallerService installer = new(http, store);
        InstalledEditor first = (await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync)).Editor;
        Assert.True(first.IsMain);
        Assert.Equal("main-aaaaaaa", first.Tag);
        Assert.Equal(first, Assert.Single(store.InstalledEditors()));
        Assert.Equal(first.Key, store.Settings.DefaultEditorKey);
        Assert.Contains(_commands, command => command.SequenceEqual(new[]
        {
            "git", "submodule", "update", "--init", "--recursive"
        }));

        _commands.Clear();
        string firstProject = ProjectTemplates.CreateBlank(Path.Combine(_home, "Projects"), "First", first);
        Project projectPin = store.AddProject(firstProject);
        projectPin.EditorKey = first.Key;
        store.Save();
        _commit = new string('b', 40);
        InstalledEditor updated = (await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync)).Editor;
        Assert.NotEqual(first.Key, updated.Key);
        Assert.Equal("main-bbbbbbb", updated.Tag);
        Assert.Equal(2, store.InstalledEditors().Count);
        Assert.Equal(first.SourceCommit, File.ReadAllText(store.ExecutablePath(first)));
        Assert.Equal(first.Key, store.Settings.DefaultEditorKey);
        Assert.Equal(first.Key, Assert.Single(new LauncherStore(_home).Settings.Projects).EditorKey);
        Assert.Equal(_commit, updated.SourceCommit);
        Assert.Equal(_commit, File.ReadAllText(store.ExecutablePath(updated)));
        Assert.DoesNotContain(_commands, command => command.Contains("clone"));
        Assert.Contains(_commands, command => command.SequenceEqual(new[]
        {
            "git", "pull", "--ff-only", "origin", "main"
        }));
        string project = ProjectTemplates.CreateBlank(Path.Combine(_home, "Projects"), "Test", updated);
        Assert.True(File.Exists(Path.Combine(project, "Test.prowl")));
        Assert.Empty(Directory.EnumerateDirectories(store.WorkPath));

        installer.Uninstall(updated);
        Assert.Equal(first, Assert.Single(store.InstalledEditors()));
        Assert.Single(Directory.EnumerateDirectories(Path.Combine(_home, "Source")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledBuild_PreservesInstalledEditor(bool cancel)
    {
        LauncherStore store = new(_home);
        using HttpClient http = new();
        EditorInstallerService installer = new(http, store);
        InstalledEditor previous = (await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync)).Editor;
        string content = File.ReadAllText(store.ExecutablePath(previous));
        _commit = new string('b', 40);
        _cancelBuild = cancel;
        _failBuild = !cancel;
        if (cancel)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() =>
                installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync));
        }
        Assert.Equal(previous, Assert.Single(store.InstalledEditors()));
        Assert.Equal(content, File.ReadAllText(store.ExecutablePath(previous)));
        Assert.Empty(Directory.EnumerateDirectories(store.WorkPath));
    }

    [Fact]
    public async Task DirtyCheckout_IsRejectedBeforePullOrBuild()
    {
        LauncherStore store = new(_home);
        using HttpClient http = new();
        EditorInstallerService installer = new(http, store);
        InstalledEditor previous = (await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync)).Editor;
        _dirty = true;
        _commands.Clear();
        await Assert.ThrowsAsync<IOException>(() =>
            installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync));
        Assert.DoesNotContain(_commands, command => command.Contains("pull") || command[0] == "dotnet");
        Assert.Equal(previous, Assert.Single(store.InstalledEditors()));
    }

    [Fact]
    public async Task InstalledCommit_SkipsBuildWithoutChangingMetadata()
    {
        LauncherStore store = new(_home);
        using HttpClient http = new();
        EditorInstallerService installer = new(http, store);
        EditorInstallerService.SourceBuildResult first = await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync);
        Assert.False(first.AlreadyInstalled);
        _commands.Clear();

        EditorInstallerService.SourceBuildResult repeated = await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync);
        Assert.True(repeated.AlreadyInstalled);
        Assert.Equal(first.Editor, repeated.Editor);
        Assert.Equal(first.Editor, Assert.Single(store.InstalledEditors()));
        Assert.Contains(_commands, command => command.Contains("pull"));
        Assert.DoesNotContain(_commands, command => command[0] == "dotnet" || command.Contains("submodule"));
        Assert.Empty(Directory.EnumerateDirectories(store.WorkPath));
    }

    [Theory]
    [InlineData("executable")]
    [InlineData("Prowl.Editor.dll")]
    [InlineData("Prowl.Editor.runtimeconfig.json")]
    public async Task MissingBuildFile_RebuildsTheInstalledCommit(string file)
    {
        LauncherStore store = new(_home);
        using HttpClient http = new();
        EditorInstallerService installer = new(http, store);
        InstalledEditor first = (await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync)).Editor;
        string missing = file == "executable"
            ? store.ExecutablePath(first)
            : Path.Combine(store.InstallPath(first), file);
        File.Delete(missing);
        _commands.Clear();

        EditorInstallerService.SourceBuildResult repaired = await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync);
        Assert.False(repaired.AlreadyInstalled);
        Assert.Equal(first.Key, repaired.Editor.Key);
        Assert.Contains(_commands, command => command[0] == "dotnet");
        Assert.True(File.Exists(store.ExecutablePath(repaired.Editor)));
        Assert.True(File.Exists(missing));
        Assert.Single(store.InstalledEditors());
    }

    [Fact]
    public async Task LegacyMainInstallation_IsRecognizedWhenItsCommitIsAlreadyInstalled()
    {
        LauncherStore store = new(_home);
        using HttpClient http = new();
        EditorInstallerService installer = new(http, store);
        InstalledEditor first = (await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync)).Editor;
        InstalledEditor legacy = first with { Tag = "main" };
        Directory.Move(store.InstallPath(first), store.InstallPath(legacy));
        LauncherStore.WriteJson(Path.Combine(store.InstallPath(legacy), "installation.json"), legacy);
        _commands.Clear();

        EditorInstallerService.SourceBuildResult repeated = await installer.InstallMainAsync(Platform.Identifier, runCommand: RunAsync);
        Assert.True(repeated.AlreadyInstalled);
        Assert.Equal(legacy, repeated.Editor);
        Assert.DoesNotContain(_commands, command => command[0] == "dotnet");
    }

    [Fact]
    public void MainInstallation_DoesNotAllowOtherNonReleaseRecords()
    {
        LauncherStore store = new(_home);
        InstalledEditor main = new(0, "main", Platform.Identifier, "Prowl.Editor", DateTimeOffset.UtcNow);
        Assert.StartsWith(store.VersionsPath, store.InstallPath(main));
        Assert.Throws<InvalidDataException>(() => store.InstallPath(main with { Tag = "v1.0.0" }));
        Assert.Throws<InvalidDataException>(() => store.InstallPath(main with { ReleaseId = -1 }));
        InstalledEditor commit = main with { Tag = "main-" + _commit, SourceCommit = _commit };
        Assert.StartsWith(store.VersionsPath, store.InstallPath(commit));
        Assert.Contains(_commit, commit.Key);
        Assert.Equal(commit.Tag, commit.ToString());
        InstalledEditor shortName = commit with { Tag = "main-" + _commit[..7] };
        Assert.Equal(commit.Key, shortName.Key);
        Assert.Equal("main-aaaaaaa", shortName.ToString());
        Assert.Throws<InvalidDataException>(() => store.InstallPath(commit with { Tag = "main-../outside" }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }
}
