using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class LauncherInstallationTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "ProwlInstallTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MigratingWindowsInstallationRetargetsOwnedShortcutsAndPreservesUpdates()
    {
        if (!OperatingSystem.IsWindows()) return;
        LauncherStore store = new(Path.Combine(_work, "Settings"));
        string root = Path.Combine(_work, "Programs", "Prowl Launcher");
        string previous = Path.Combine(root, "1.0.0-preview-6", "Prowl.Launcher.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
        File.WriteAllText(previous, "bootstrap");
        store.Settings.InstalledLauncherExecutable = previous;
        store.Settings.LauncherExecutable = Path.Combine(_work, "Updates", "current.exe");
        string shortcut = Path.Combine(_work, "Desktop", "Prowl Launcher.lnk");
        string custom = Path.Combine(_work, "Custom", "Prowl Launcher.lnk");
        string missing = Path.Combine(_work, "Missing.lnk");
        LauncherInstallationService.CreateWindowsShortcut(shortcut, previous);
        LauncherInstallationService.CreateWindowsShortcut(custom, Path.Combine(_work, "custom.exe"));
        byte[] customContents = File.ReadAllBytes(custom);

        new LauncherInstallationService(store).MigrateWindowsInstallation(root, [shortcut, custom, missing]);

        string stable = Path.Combine(root, "Prowl.Launcher.exe");
        Assert.Equal("bootstrap", File.ReadAllText(stable));
        Assert.True(File.Exists(previous));
        Assert.Equal(stable, new LauncherStore(store.Home).Settings.InstalledLauncherExecutable);
        Assert.Equal(Path.Combine(_work, "Updates", "current.exe"), store.Settings.LauncherExecutable);
        Assert.Equal(customContents, File.ReadAllBytes(custom));
        Assert.False(File.Exists(missing));
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        object shell = Activator.CreateInstance(shellType)!;
        object? link = null;
        try
        {
            dynamic result = link = ((dynamic)shell).CreateShortcut(shortcut);
            Assert.True(LauncherStore.PathsEqual(stable, (string)result.TargetPath));
            Assert.Equal(stable + ",0", (string)result.IconLocation);
        }
        finally
        {
            if (link != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    [Fact]
    public void InstallingCopiesTheDownloadWithoutRemovingIt()
    {
        Directory.CreateDirectory(_work);
        string source = Path.Combine(_work, "download.exe");
        string target = Path.Combine(_work, "Application", "Prowl.Launcher.exe");
        File.WriteAllText(source, "application");
        LauncherInstallationService.CopyApplication(source, target, false, default);
        Assert.Equal("application", File.ReadAllText(target));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.tmp"));
    }

    [Fact]
    public void InstallingNeverOverwritesAnExistingApplication()
    {
        Directory.CreateDirectory(_work);
        string source = Path.Combine(_work, "download.exe");
        string target = Path.Combine(_work, "installed.exe");
        File.WriteAllText(source, "new");
        File.WriteAllText(target, "existing");
        Assert.Throws<IOException>(() => LauncherInstallationService.CopyApplication(source, target, false, default));
        Assert.Equal("existing", File.ReadAllText(target));
    }

    [Fact]
    public void CancellingBeforeInstallCreatesNoFiles()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => LauncherInstallationService.CopyApplication(
            Path.Combine(_work, "download.exe"), Path.Combine(_work, "installed.exe"), false, cancelled.Token));
        Assert.False(Directory.Exists(_work));
    }

    [Fact]
    public void MissingDownloadLeavesNoPartialInstallation()
    {
        string target = Path.Combine(_work, "Application", "Prowl.Launcher.exe");
        Assert.Throws<FileNotFoundException>(() => LauncherInstallationService.CopyApplication(
            Path.Combine(_work, "missing.exe"), target, false, default));
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!));
    }

    [Fact]
    public void InstallingABundleKeepsItsContentsAndExecutablePermissions()
    {
        string source = Path.Combine(_work, "Download.app");
        string relative = Path.Combine("Contents", "MacOS", "Prowl.Launcher");
        string executable = Path.Combine(source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "application");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        string target = Path.Combine(_work, "Installed.app");
        LauncherInstallationService.CopyApplication(source, target, true, default);
        Assert.Equal("application", File.ReadAllText(Path.Combine(target, relative)));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(Path.Combine(target, relative)).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public void LinuxShortcutQuotesSpacesAndEscapesExpansionCharacters()
    {
        string executable = "/home/a $name/100%/Prowl Launcher.AppImage";
        string entry = LauncherInstallationService.DesktopEntry(executable, "/home/a/icons/prowl.png");
        Assert.Contains("Exec=\"/home/a \\\\$name/100%%/Prowl Launcher.AppImage\"\n", entry);
        Assert.Contains("Terminal=false", entry);
        Assert.Contains("Icon=/home/a/icons/prowl.png", entry);
    }

    [Fact]
    public void SourceBuildDoesNotOfferSelfInstallation()
    {
        LauncherStore store = new( _work );
        Assert.False(LauncherInstallationService.ShouldOffer(store));
    }

    [Fact]
    public void RetryingCanReuseAnIdenticalCopyButRejectsChangedContents()
    {
        Directory.CreateDirectory(_work);
        string source = Path.Combine(_work, "download.exe");
        string target = Path.Combine(_work, "installed.exe");
        File.WriteAllText(source, "original");
        Assert.False(LauncherInstallationService.SameApplication(source, target, false, default));
        File.Copy(source, target);
        Assert.True(LauncherInstallationService.SameApplication(source, target, false, default));
        File.WriteAllText(target, "modified");
        Assert.False(LauncherInstallationService.SameApplication(source, target, false, default));
    }

    [Fact]
    public void WindowsShortcutTargetsTheInstalledAppIncludingPathsWithSpaces()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        string executable = Path.Combine(_work, "Prowl Launcher", "Prowl.Launcher.exe");
        string shortcut = Path.Combine(_work, "Desktop", "Prowl Launcher.lnk");
        LauncherInstallationService.CreateWindowsShortcut(shortcut, executable);
        Assert.True(File.Exists(shortcut));
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
                         ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable.");
        object shell = Activator.CreateInstance(shellType)
                       ?? throw new InvalidOperationException("Windows Script Host could not be started.");
        object? link = null;
        try
        {
            dynamic result = link = ((dynamic)shell).CreateShortcut(shortcut);
            Assert.True(LauncherStore.PathsEqual(executable, (string)result.TargetPath));
            Assert.True(LauncherStore.PathsEqual(Path.GetDirectoryName(executable)!, (string)result.WorkingDirectory));
        }
        finally
        {
            if (link != null)
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
            }
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, true);
        }
    }
}
