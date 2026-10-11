using Xunit;
namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class ProjectRelocationTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlRelocationTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }

    private string CreateProject(string name)
    {
        string path = Path.Combine(_home, "Projects", name);
        Directory.CreateDirectory(Path.Combine(path, "Assets"));
        JsonFile.Write(Path.Combine(path, name + ".prowl"), new
        {
            name, version = "1.0"
        });
        return path;
    }

    [Fact]
    public void LocatingAMovedProjectKeepsItsEditorFavouriteAndHistory()
    {
        LauncherStore store = new( Path.Combine(_home, "Home") );
        string original = CreateProject("Game");
        Project project = store.AddProject(original);
        project.EditorKey = "v1.0.0-win-x64";
        project.Favorite = true;
        project.LastOpened = DateTimeOffset.UtcNow.AddDays(-1);
        store.Save();

        string moved = Path.Combine(_home, "Elsewhere", "Game");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(original, moved);
        store.RelocateProject(project, Path.Combine(moved, "Game.prowl"));

        Project saved = Assert.Single(new LauncherStore(store.Home).Settings.Projects);
        Assert.Equal(moved, saved.Path);
        Assert.Equal("v1.0.0-win-x64", saved.EditorKey);
        Assert.True(saved.Favorite);
        Assert.Equal(project.LastOpened, saved.LastOpened);
    }

    [Fact]
    public void LocatingRejectsFoldersThatArentProjectsOrAreAlreadyListed()
    {
        LauncherStore store = new( Path.Combine(_home, "Home") );
        Project project = store.AddProject(CreateProject("Game"));
        string other = CreateProject("Other");
        store.AddProject(other);
        string empty = Path.Combine(_home, "Empty");
        Directory.CreateDirectory(empty);

        Assert.Throws<InvalidDataException>(() => store.RelocateProject(project, empty));
        Assert.Throws<InvalidOperationException>(() => store.RelocateProject(project, other));
        Assert.Equal(2, new LauncherStore(store.Home).Settings.Projects.Count);
        Assert.EndsWith("Game", project.Path);
    }
}
