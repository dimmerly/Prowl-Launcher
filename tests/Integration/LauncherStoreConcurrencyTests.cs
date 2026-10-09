using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class LauncherStoreConcurrencyTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlSettingsTests", Guid.NewGuid().ToString("N"));
    private string ProjectFolder(string name)
    {
        string path = Path.Combine(_home, name);
        Directory.CreateDirectory(Path.Combine(path, "Assets"));
        return path;
    }

    [Fact]
    public void StaleSettingsSavePreservesOtherInstancesProjects()
    {
        LauncherStore first = new(_home), second = new(_home);
        first.AddProject(ProjectFolder("Game"));
        second.Settings.Locale = "fr";
        second.Save();
        LauncherStore saved = new(_home);
        Assert.Single(saved.Settings.Projects);
        Assert.Equal("fr", saved.Settings.Locale);
        first.Settings.ShowFps = true;
        first.Save();
        Assert.Equal("fr", first.Settings.Locale);
    }

    [Fact]
    public void ConcurrentProjectEditsMergeByFieldAndKeepLiveReferences()
    {
        LauncherStore first = new(_home);
        Project original = first.AddProject(ProjectFolder("Game"));
        LauncherStore second = new(_home);
        original.Favorite = true;
        first.Save();
        second.Settings.Projects[0].EditorKey = "pinned-editor";
        second.Save();
        first.Settings.ShowFps = true;
        first.Save();
        Assert.Same(original, first.Settings.Projects[0]);
        Assert.True(original.Favorite);
        Assert.Equal("pinned-editor", original.EditorKey);
    }

    [Fact]
    public void UnchangedStaleProjectDoesNotUndoRemoval()
    {
        LauncherStore first = new(_home);
        first.AddProject(ProjectFolder("Game"));
        LauncherStore second = new(_home);
        first.Settings.Projects.Clear();
        first.Save();
        second.Settings.Locale = "fr";
        second.Save();
        Assert.Empty(new LauncherStore(_home).Settings.Projects);
    }

    [Fact]
    public async Task SimultaneousSavesPreserveBothAddedProjects()
    {
        LauncherStore first = new(_home), second = new(_home);
        first.Settings.Projects.Add(new Project { Path = ProjectFolder("One"), Name = "One" });
        second.Settings.Projects.Add(new Project { Path = ProjectFolder("Two"), Name = "Two" });
        await Task.WhenAll(Task.Run(first.Save), Task.Run(second.Save));
        Assert.Equal(2, new LauncherStore(_home).Settings.Projects.Count);
    }

    public void Dispose() { if (Directory.Exists(_home)) Directory.Delete(_home, true); }
}
