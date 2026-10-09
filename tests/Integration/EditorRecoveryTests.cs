using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class EditorRecoveryTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlRecoveryTests", Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();

    private (LauncherStore store, InstalledEditor editor, string work) InterruptedRepair()
    {
        LauncherStore store = new( _home );
        InstalledEditor editor = new( 1, "v1.0.0", "win-x64", "Prowl.Editor.exe", DateTimeOffset.UtcNow );
        string work = Path.Combine(store.WorkPath, Guid.NewGuid().ToString("N"));
        string previous = Path.Combine(work, "previous");
        LauncherStore.WriteJson(Path.Combine(previous, "installation.json"), editor);
        File.WriteAllText(Path.Combine(previous, editor.ExecutableRelativePath), "old editor");
        LauncherStore.WriteJson(Path.Combine(work, "repair.json"), editor);
        return (store, editor, work);
    }

    [Fact]
    public void StartupRestoresTheOldEditorAfterInterruptionBetweenMoves()
    {
        (LauncherStore store, InstalledEditor editor, string work) = InterruptedRepair();
        new EditorInstallerService(_http, store).RecoverInterruptedOperations();
        Assert.Equal("old editor", File.ReadAllText(store.ExecutablePath(editor)));
        Assert.Single(store.InstalledEditors());
        Assert.False(Directory.Exists(work));
    }

    [Fact]
    public void FailedRestorationRetainsBackupAndJournalForTheNextAttempt()
    {
        (LauncherStore store, InstalledEditor editor, string work) = InterruptedRepair();
        EditorInstallerService installer = new( _http, store );
        installer.RecoverInterruptedInstalls((_, _) => throw new IOException("Locked destination"));
        Assert.True(File.Exists(Path.Combine(work, "repair.json")));
        Assert.Equal("old editor", File.ReadAllText(Path.Combine(work, "previous", editor.ExecutableRelativePath)));
        installer.RecoverInterruptedOperations();
        Assert.Equal("old editor", File.ReadAllText(store.ExecutablePath(editor)));
    }

    [Fact]
    public void CommittedRepairKeepsTheNewEditorAndRemovesOnlyItsBackup()
    {
        (LauncherStore store, InstalledEditor editor, string work) = InterruptedRepair();
        LauncherStore.WriteJson(Path.Combine(store.InstallPath(editor), "installation.json"), editor);
        File.WriteAllText(store.ExecutablePath(editor), "new editor");
        string sample = Path.Combine(store.WorkPath, "sample-running", "data.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sample)!);
        File.WriteAllText(sample, "sample");
        new EditorInstallerService(_http, store).RecoverInterruptedOperations();
        Assert.Equal("new editor", File.ReadAllText(store.ExecutablePath(editor)));
        Assert.False(Directory.Exists(work));
        Assert.True(File.Exists(sample));
    }

    [Fact]
    public void IncompleteDestinationNeverCausesTheOldEditorToBeDeleted()
    {
        (LauncherStore store, InstalledEditor editor, string work) = InterruptedRepair();
        Directory.CreateDirectory(store.InstallPath(editor));
        new EditorInstallerService(_http, store).RecoverInterruptedOperations();
        Assert.Equal("old editor", File.ReadAllText(Path.Combine(work, "previous", editor.ExecutableRelativePath)));
        Assert.True(File.Exists(Path.Combine(work, "repair.json")));
    }

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }
}
