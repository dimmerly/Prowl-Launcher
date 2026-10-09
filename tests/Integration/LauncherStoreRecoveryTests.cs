using System.Text.Json;
using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class LauncherStoreRecoveryTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlSettingsRecovery", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_home, "settings.json");

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Projects\":42}")]
    public void DamagedSettingsArePreservedAndRestoredFromTheLatestBackup(string damage)
    {
        LauncherStore original = new(_home);
        original.Settings.Locale = "fr";
        original.Settings.Projects.Add(new Project { Path = Path.Combine(_home, "Game"), Name = "Game", Favorite = true });
        original.Save();
        File.WriteAllText(SettingsPath, damage);

        LauncherStore recovered = new(_home);
        Assert.Equal("fr", recovered.Settings.Locale);
        Assert.True(Assert.Single(recovered.Settings.Projects).Favorite);
        recovered.Save();
        string preserved = Assert.Single(Directory.GetFiles(_home, "settings.json.corrupt-*"));
        Assert.Equal(damage, File.ReadAllText(preserved));
        Assert.Equal("fr", new LauncherStore(_home).Settings.Locale);
    }

    [Fact]
    public void DamageWithoutABackupIsPreservedBeforeSavingDefaults()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(SettingsPath, "{broken");
        LauncherStore store = new(_home);
        store.Save();
        Assert.Empty(store.Settings.Projects);
        Assert.Equal("{broken", File.ReadAllText(Assert.Single(Directory.GetFiles(_home, "settings.json.corrupt-*"))));
    }

    [Fact]
    public void NullCollectionsAndRepositoriesAreNormalized()
    {
        Directory.CreateDirectory(_home);
        File.WriteAllText(SettingsPath, "{\"Projects\":null,\"DismissedLauncherReleases\":null,\"ProwlRepository\":null,\"LauncherRepository\":\"../bad\"}");
        LauncherStore store = new(_home);
        store.Save();
        Assert.Empty(store.Settings.Projects);
        Assert.Empty(store.Settings.DismissedLauncherReleases);
        Assert.Equal(GitHubRepositoryHelper.DefaultProwl, store.Settings.ProwlRepository);
        Assert.Equal(GitHubRepositoryHelper.LauncherRepository, store.Settings.LauncherRepository);
        Assert.Single(Directory.GetFiles(_home, "settings.json.corrupt-*"));
    }

    [Fact]
    public void InvalidProjectsAreRemovedAndDuplicateMetadataIsMerged()
    {
        Directory.CreateDirectory(_home);
        string projectPath = Path.Combine(_home, "Game");
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new
        {
            Projects = new Project?[] { null, new() { Path = "" }, new() { Path = projectPath, Name = "Game" },
                new() { Path = projectPath, Favorite = true, EditorKey = "pinned", LastOpened = DateTimeOffset.UtcNow } }
        }));
        LauncherStore store = new(_home);
        Project project = Assert.Single(store.Settings.Projects);
        Assert.True(project.Favorite);
        Assert.Equal("pinned", project.EditorKey);
        store.Save();
        Assert.Same(project, Assert.Single(store.Settings.Projects));
        Assert.Single(new LauncherStore(_home).Settings.Projects);
    }

    [Fact]
    public void AStaleInstanceRecoversDiskDamageWithoutLosingItsEdits()
    {
        LauncherStore store = new(_home);
        store.Settings.Locale = "fr";
        store.Save();
        store.Settings.ShowFps = true;
        File.WriteAllText(SettingsPath, "{broken");
        store.Save();
        LauncherStore saved = new(_home);
        Assert.Equal("fr", saved.Settings.Locale);
        Assert.True(saved.Settings.ShowFps);
    }

    public void Dispose() { if (Directory.Exists(_home)) Directory.Delete(_home, true); }
}
