using System.Text.Json;

using Prowl.Launcher;

using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Unit")]
public sealed class ProjectTemplateTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlLauncherTemplates", Guid.NewGuid().ToString("N"));
    private static InstalledEditor Editor => new( 1, "v1.0-preview-4", "win-x64", "Prowl.Editor.exe", DateTimeOffset.UtcNow );

    [Fact]
    public void BlankProjectUsesSelectedEditorVersion_AndImportsWithCorrectPin()
    {
        string path = ProjectTemplates.CreateBlank(_home, "My game", Editor);
        Assert.True(Directory.Exists(Path.Combine(path, "Assets")));
        using JsonDocument marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "My game.prowl")));
        Assert.Equal("1.0-preview.4", marker.RootElement.GetProperty("version").GetString());
        Assert.False(marker.RootElement.TryGetProperty("appliedSteps", out _));
        Assert.True(File.Exists(Path.Combine(path, "Directory.Build.props")));
        LauncherStore store = new( Path.Combine(_home, "Launcher") );
        LauncherStore.WriteJson(Path.Combine(store.InstallPath(Editor), "installation.json"), Editor);
        Assert.Equal(Editor.Key, store.AddProject(path).EditorKey);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("C:\\escape")]
    [InlineData(".")]
    [InlineData("CON")]
    [InlineData("bad/name")]
    [InlineData("")]
    public void InvalidNamesDoNotCreateDirectories(string name)
    {
        Assert.Throws<InvalidDataException>(() => ProjectTemplates.CreateBlank(_home, name, Editor));
        Assert.False(Directory.Exists(_home));
    }

    [Fact]
    public void CreationNeverOverwritesExistingProjects()
    {
        string path = ProjectTemplates.CreateBlank(_home, "Existing", Editor);
        string asset = Path.Combine(path, "Assets", "Keep.txt");
        File.WriteAllText(asset, "keep");
        Assert.Throws<IOException>(() => ProjectTemplates.CreateBlank(_home, "Existing", Editor));
        Assert.Equal("keep", File.ReadAllText(asset));
        Assert.Empty(Directory.EnumerateDirectories(_home, ".prowl-create-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }
}
