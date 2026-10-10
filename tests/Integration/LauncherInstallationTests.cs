using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class LauncherInstallationTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "ProwlInstallTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultEditorsInstallAlongsideTheLauncherFolder()
    {
        string prowl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prowl");
        Assert.Equal(Path.Combine(prowl, "Versions"), Platform.DefaultVersionsPath);
        Assert.Equal(Path.Combine(prowl, "Launcher"), Platform.DefaultHome);
    }

    [Fact]
    public void CustomHomeKeepsEditorInstallationsIsolated()
    {
        LauncherStore store = new(Path.Combine(_work, "Portable"));
        Assert.Equal(Path.Combine(store.Home, "Versions"), store.VersionsPath);
    }

    [Fact]
    public void WindowsInstallationUsesTheLauncherHomeFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        LauncherStore store = new(Path.Combine(_work, "Roaming", "Prowl", "Launcher"));
        Assert.Equal(store.Home, LauncherInstallationService.InstallRoot(store));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prowl", "Launcher"),
            Platform.DefaultHome);
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
        Assert.Contains("Name=Prowl\n", entry);
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
        string shortcut = Path.Combine(_work, "Desktop", "Prowl.lnk");
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
            Assert.Equal("Prowl", (string)result.Description);
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
