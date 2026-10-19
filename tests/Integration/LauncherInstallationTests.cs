using System.Runtime.InteropServices;

using Microsoft.Win32;

using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class LauncherInstallationTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "ProwlInstallTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, true);
        }
    }

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
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        LauncherStore store = new(Path.Combine(_work, "Roaming", "Prowl", "Launcher"));
        Assert.Equal(store.Home, LauncherInstallationService.InstallRoot(store));
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prowl", "Launcher"),
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
        LauncherStore store = new(_work);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UninstallingRemovesTheLauncherButKeepsEditorsAndProjects(bool removeData)
    {
        LauncherStore store = new(Path.Combine(_work, "Home"));
        string desktop = Path.Combine(_work, "Desktop"), shortcuts = Path.Combine(_work, "Shortcuts");
        string application = OperatingSystem.IsMacOS()
            ? Path.Combine(_work, "Applications", "Prowl Launcher.app", "Contents", "MacOS", "Prowl.Launcher")
            : OperatingSystem.IsWindows()
                ? Path.Combine(store.Home, "Prowl.Launcher.exe")
                : Path.Combine(LauncherInstallationService.InstallRoot(store), "1.0.0", "Prowl Launcher.AppImage");
        string shortcut = OperatingSystem.IsWindows() ? Path.Combine(desktop, "Prowl.lnk")
            : OperatingSystem.IsMacOS() ? Path.Combine(desktop, "Prowl.app") : Path.Combine(desktop, "Prowl.desktop");
        string otherApp = Path.Combine(_work, "Applications", "Other.app", "keep.txt");
        string editor = Path.Combine(store.VersionsPath, "v1.0.0-win-x64", "installation.json");
        string update = Path.Combine(LauncherUpdaterService.UpdatesPath(store), "repository", "2.0.0",
            "Prowl.Launcher");
        foreach (string file in new[] { application, shortcut, otherApp, editor, update })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "file");
        }

        store.Settings.InstalledLauncherExecutable = application;
        store.Save();

        IReadOnlyList<string> paths = LauncherInstallationService.InstalledPaths(store, removeData, desktop, shortcuts);
        Assert.Empty(LauncherInstallationService.DeletePaths(paths));

        Assert.False(File.Exists(application));
        Assert.False(File.Exists(shortcut));
        Assert.False(Directory.Exists(LauncherUpdaterService.UpdatesPath(store)));
        Assert.True(File.Exists(editor));
        Assert.True(File.Exists(otherApp));
        Assert.Equal(!removeData, File.Exists(store.SettingsPath));
    }

    [Fact]
    public void UninstallingRemovesLinksWithoutFollowingThem()
    {
        string target = Path.Combine(_work, "Target");
        string link = Path.Combine(_work, "Desktop", "Prowl.app");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return; // Windows needs developer mode or elevation to create links.
        }

        Assert.Empty(LauncherInstallationService.DeletePaths([link]));
        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void WindowsUninstallerEntryRunsTheLauncherUninstallPrompt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string key = @"Software\ProwlLauncherTests\" + Guid.NewGuid().ToString("N");
        string executable = Path.Combine(_work, "Prowl Launcher", "Prowl.Launcher.exe");
        try
        {
            LauncherInstallationService.RegisterWindowsUninstaller(executable, _work, key);
            using RegistryKey entry = Registry.CurrentUser.OpenSubKey(key)!;
            Assert.Equal("Prowl Launcher", entry.GetValue("DisplayName"));
            Assert.Equal($"\"{executable}\" --uninstall", entry.GetValue("UninstallString"));
            Assert.Equal(_work, entry.GetValue("InstallLocation"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\ProwlLauncherTests", false);
        }
    }

    [Fact]
    public void WindowsProjectFilesOpenWithTheLauncherUntilItIsUninstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string classes = @"Software\ProwlLauncherTests\" + Guid.NewGuid().ToString("N");
        string executable = Path.Combine(_work, "Prowl Launcher", "Prowl.Launcher.exe");
        try
        {
            LauncherInstallationService.RegisterWindowsFileAssociation(executable, classes);
            using (RegistryKey command =
                   Registry.CurrentUser.OpenSubKey($@"{classes}\ProwlLauncher.Project\shell\open\command")!)
            {
                Assert.Equal($"\"{executable}\" \"%1\"", command.GetValue(""));
            }

            LauncherInstallationService.UnregisterWindowsFileAssociation(classes);
            Assert.Null(Registry.CurrentUser.OpenSubKey($@"{classes}\.prowl"));
            Assert.Null(Registry.CurrentUser.OpenSubKey($@"{classes}\ProwlLauncher.Project"));

            // Another app took over .prowl; uninstalling must leave its association alone.
            using (RegistryKey other = Registry.CurrentUser.CreateSubKey($@"{classes}\.prowl"))
            {
                other.SetValue("", "SomeEditor.Project");
            }

            LauncherInstallationService.UnregisterWindowsFileAssociation(classes);
            using RegistryKey kept = Registry.CurrentUser.OpenSubKey($@"{classes}\.prowl")!;
            Assert.Equal("SomeEditor.Project", kept.GetValue(""));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\ProwlLauncherTests", false);
        }
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
            Assert.True(FileSystemHelper.PathsEqual(executable, (string)result.TargetPath));
            Assert.True(
                FileSystemHelper.PathsEqual(Path.GetDirectoryName(executable)!, (string)result.WorkingDirectory));
        }
        finally
        {
            if (link != null)
            {
                Marshal.FinalReleaseComObject(link);
            }

            Marshal.FinalReleaseComObject(shell);
        }
    }
}
