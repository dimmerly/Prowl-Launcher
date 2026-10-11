using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

using Microsoft.Win32;

using Prowl.Rosetta;
namespace Prowl.Launcher;

sealed class LauncherInstallationService(LauncherStore store)
{
    /// <summary>Opens the launcher on its uninstall prompt; Windows' Apps &amp; Features runs this.</summary>
    internal const string UninstallArgument = "--uninstall";
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ProwlLauncher";
    private const string LinuxEntryName = "com.prowlengine.launcher.desktop";
    private const string ProjectClass = "ProwlLauncher.Project";

    /// <summary>True for the released, self-installing build; false for source and update builds.</summary>
    internal static bool IsSelfInstallBuild { get; } = typeof( Launcher ).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(attribute => attribute.Key == "SelfInstall" && attribute.Value == "true");

    internal static bool ShouldOffer(LauncherStore store) => !store.Settings.InstallationPromptHandled && CanInstall(store);

    /// <summary>True when this is a released build that isn't installed yet.</summary>
    internal static bool CanInstall(LauncherStore store) => store.Settings.InstalledLauncherExecutable == null
                                                            && IsSelfInstallBuild
                                                            && SourcePath() != null;

    internal static string InstallRoot(LauncherStore store)
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");
        }

        if (OperatingSystem.IsWindows())
        {
            return store.Home;
        }

        return Path.Combine(store.Home, "Application");
    }

    internal static bool IsRunningInstallation(string executable) =>
        Environment.ProcessPath is {} processPath && FileSystemHelper.PathsEqual(executable, processPath)
        || SourcePath() is {} source && FileSystemHelper.PathsEqual(executable, source);

    private static string? SourcePath()
    {
        if (OperatingSystem.IsMacOS())
        {
            string bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../.."));
            return bundle.EndsWith(".app", StringComparison.Ordinal) && Directory.Exists(bundle) ? bundle : null;
        }
        if (OperatingSystem.IsLinux())
        {
            string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            return appImage != null && File.Exists(appImage) ? Path.GetFullPath(appImage) : null;
        }
        return Environment.ProcessPath;
    }

    public async Task<string> InstallAsync(bool desktopShortcut, CancellationToken token)
    {
        string source = SourcePath() ?? throw new InvalidOperationException(Loc.Get("launcher.installation.unavailable"));
        string version = VersionHelper.LauncherVersion;
        string root = InstallRoot(store);
        string fileName = OperatingSystem.IsWindows() ? "Prowl.Launcher.exe" : "Prowl Launcher.AppImage";
        string relative = OperatingSystem.IsMacOS() ? "Prowl Launcher.app"
            : OperatingSystem.IsWindows() ? fileName : Path.Combine(version, fileName);
        string destination = FileSystemHelper.SafeChildPath(root, relative);
        string executable = OperatingSystem.IsMacOS() ? Path.Combine(destination, "Contents", "MacOS", "Prowl.Launcher") : destination;

        if (!IsRunningInstallation(executable))
        {
            await Task.Run(() =>
            {
                // A previous attempt may have copied the app before shortcut creation failed.
                // The stable Windows entry point forwards to the selected update;
                // retain it across reinstalls instead of replacing a potentially running app.
                if (!(OperatingSystem.IsWindows() && File.Exists(destination))
                    && !SameApplication(source, destination, OperatingSystem.IsMacOS(), token))
                {
                    CopyApplication(source, destination, OperatingSystem.IsMacOS(), token);
                }
            }, token);
        }

        token.ThrowIfCancellationRequested();
        RegisterApplication(root, destination, executable, desktopShortcut);

        store.Settings.InstalledLauncherExecutable = executable;
        store.Settings.InstallationPromptHandled = true;
        store.Save();
        return executable;
    }

    /// <summary>
    ///     Removes the installed launcher, its shortcuts, its downloaded updates and its Apps &amp; Features entry.
    ///     <paramref name="removeData" /> also removes settings and caches. Editors and projects are always kept.
    ///     Files still in use, such as this running launcher on Windows, are removed once it exits.
    /// </summary>
    public async Task UninstallAsync(bool removeData, CancellationToken token)
    {
        IReadOnlyList<string> paths = InstalledPaths(store, removeData, DesktopDirectory(),
            OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.Programs) : ApplicationsDirectory());
        // Reset even when the settings are deleted, so a later save can't bring the installation back.
        store.Settings.InstalledLauncherExecutable = null;
        store.Settings.LauncherExecutable = null;
        store.Settings.InstallationPromptHandled = false;
        if (!removeData)
        {
            store.Save();
        }
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            UnregisterWindowsFileAssociation();
        }

        IReadOnlyList<string> inUse = await Task.Run(() => DeletePaths(paths, token), token);
        if (inUse.Count > 0)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new IOException(Loc.Get("launcher.uninstall.failed", new
                {
                    path = inUse[0]
                }));
            }
            DeleteAfterExit(inUse);
        }
    }

    /// <summary>
    ///     What uninstalling removes. <paramref name="shortcuts" /> is the Start menu folder on Windows and the
    ///     applications folder for desktop entries on Linux.
    /// </summary>
    internal static IReadOnlyList<string> InstalledPaths(LauncherStore store, bool removeData, string desktop, string shortcuts)
    {
        List<string> paths = [];
        if (OperatingSystem.IsWindows())
        {
            paths.Add(Path.Combine(shortcuts, "Prowl.lnk"));
            paths.Add(Path.Combine(desktop, "Prowl.lnk"));
        }
        else if (OperatingSystem.IsLinux())
        {
            paths.Add(Path.Combine(shortcuts, LinuxEntryName));
            paths.Add(Path.Combine(desktop, "Prowl.desktop"));
            paths.Add(InstallRoot(store));
        }
        else if (OperatingSystem.IsMacOS())
        {
            paths.Add(Path.Combine(desktop, "Prowl.app"));
        }

        if (store.Settings.InstalledLauncherExecutable is {} executable)
        {
            // Only the launcher's own files: the macOS install root is the shared ~/Applications folder.
            string application = OperatingSystem.IsMacOS() ? Path.GetFullPath(Path.Combine(executable, "..", "..", "..")) : executable;
            if (Path.GetFileName(application) is "Prowl.Launcher.exe" or "Prowl Launcher.app" or "Prowl Launcher.AppImage")
            {
                paths.Add(application);
            }
        }
        paths.Add(LauncherUpdaterService.UpdatesPath(store));

        if (removeData)
        {
            // A custom home keeps its editors inside it; those stay.
            string versions = Path.GetFullPath(store.VersionsPath);
            if (FileSystemHelper.IsInside(store.Home, versions))
            {
                paths.AddRange(Directory.EnumerateFileSystemEntries(store.Home)
                    .Where(path => !FileSystemHelper.PathsEqual(path, versions)));
            }
            else
            {
                paths.Add(store.Home);
            }
        }
        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Deletes each path that exists, links without following them, and returns those still in use.</summary>
    internal static IReadOnlyList<string> DeletePaths(IEnumerable<string> paths, CancellationToken token = default)
    {
        List<string> inUse = [];
        foreach (string path in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    continue;
                }
                if (FileSystemHelper.IsLink(path) || File.Exists(path))
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                else
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                inUse.Add(path);
            }
        }
        return inUse;
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteAfterExit(IReadOnlyList<string> paths)
    {
        string quoted = string.Join(",", paths.Select(path => "'" + path.Replace("'", "''") + "'"));
        // PowerShell removes nothing when its working directory is inside a target, and the launcher's
        // working directory is usually its own folder.
        ProcessStartInfo cleanup = new("powershell.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        };
        foreach (string argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command",
                     $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Sleep -Seconds 1; "
                     + $"Remove-Item -LiteralPath {quoted} -Recurse -Force -ErrorAction SilentlyContinue"
                 })
        {
            cleanup.ArgumentList.Add(argument);
        }
        Process.Start(cleanup);
    }

    [SupportedOSPlatform("windows")]
    internal static void RegisterWindowsUninstaller(string executable, string root, string keyPath = UninstallKey)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue("DisplayName", "Prowl Launcher");
        key.SetValue("DisplayVersion", VersionHelper.LauncherVersion);
        key.SetValue("Publisher", "Prowl");
        key.SetValue("DisplayIcon", executable + ",0");
        key.SetValue("InstallLocation", root);
        key.SetValue("UninstallString", $"\"{executable}\" {UninstallArgument}");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>Opens .prowl files with the launcher, which adds and opens their project.</summary>
    [SupportedOSPlatform("windows")]
    internal static void RegisterWindowsFileAssociation(string executable, string classesPath = @"Software\Classes")
    {
        using (RegistryKey extension = Registry.CurrentUser.CreateSubKey($@"{classesPath}\.prowl"))
        {
            extension.SetValue("", ProjectClass);
        }
        using RegistryKey type = Registry.CurrentUser.CreateSubKey($@"{classesPath}\{ProjectClass}");
        type.SetValue("", "Prowl project");
        using (RegistryKey icon = type.CreateSubKey("DefaultIcon"))
        {
            icon.SetValue("", executable + ",0");
        }
        using (RegistryKey command = type.CreateSubKey(@"shell\open\command"))
        {
            command.SetValue("", $"\"{executable}\" \"%1\"");
        }
        NotifyAssociationsChanged();
    }

    /// <summary>Removes the launcher's .prowl association, leaving one another app has taken over alone.</summary>
    [SupportedOSPlatform("windows")]
    internal static void UnregisterWindowsFileAssociation(string classesPath = @"Software\Classes")
    {
        bool ours;
        using (RegistryKey? extension = Registry.CurrentUser.OpenSubKey($@"{classesPath}\.prowl"))
        {
            ours = extension?.GetValue("") as string == ProjectClass;
        }
        if (ours)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{classesPath}\.prowl", false);
        }
        Registry.CurrentUser.DeleteSubKeyTree($@"{classesPath}\{ProjectClass}", false);
        NotifyAssociationsChanged();
    }

    [SupportedOSPlatform("windows")]
    private static void NotifyAssociationsChanged()
    {
        const int AssociationsChanged = 0x08000000;
        SHChangeNotify(AssociationsChanged, 0, 0, 0);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    private static string ApplicationsDirectory() => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_DATA_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "applications");

    private static void RegisterApplication(string root, string destination, string executable, bool desktopShortcut)
    {
        // Register the app even when the optional desktop shortcut is disabled.
        if (OperatingSystem.IsWindows())
        {
            RegisterWindowsApplication(root, executable, desktopShortcut);
        }
        else if (OperatingSystem.IsLinux())
        {
            RegisterLinuxApplication(root, executable, desktopShortcut);
        }
        else if (OperatingSystem.IsMacOS() && desktopShortcut)
        {
            CreateMacShortcut(destination);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindowsApplication(string root, string executable, bool desktopShortcut)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        CreateWindowsShortcut(Path.Combine(programs, "Prowl.lnk"), executable);
        RegisterWindowsUninstaller(executable, root);
        RegisterWindowsFileAssociation(executable);

        if (desktopShortcut)
        {
            CreateWindowsShortcut(Path.Combine(DesktopDirectory(), "Prowl.lnk"), executable);
        }
    }

    private static void RegisterLinuxApplication(string root, string executable, bool desktopShortcut)
    {
        string icon = Path.Combine(root, "prowl.png");
        using Stream image = typeof( Launcher ).Assembly.GetManifestResourceStream("Prowl.Launcher.prowl.png")
                             ?? throw new InvalidDataException("The launcher icon is missing.");
        using (FileStream file = File.Create(icon))
        {
            image.CopyTo(file);
        }

        string entry = DesktopEntry(executable, icon);
        WriteDesktopEntry(Path.Combine(ApplicationsDirectory(), LinuxEntryName), entry);

        if (desktopShortcut)
        {
            WriteDesktopEntry(Path.Combine(DesktopDirectory(), "Prowl.desktop"), entry);
        }
    }

    private static void CreateMacShortcut(string destination)
    {
        string shortcut = Path.Combine(DesktopDirectory(), "Prowl.app");
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);

        if (!Directory.Exists(shortcut) && !File.Exists(shortcut))
        {
            Directory.CreateSymbolicLink(shortcut, destination);
        }
    }

    internal static void CopyApplication(string source, string destination, bool bundle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException(Loc.Get("launcher.installation.already_exists"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (bundle)
            {
                CopyBundle(source, temporary, token);
                token.ThrowIfCancellationRequested();
                Directory.Move(temporary, destination);
            }
            else
            {
                CopyFile(source, temporary);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination);
            }
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
            else if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void CopyBundle(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if (FileSystemHelper.IsLink(path))
            {
                throw new IOException(Loc.Get("launcher.errors.archive_link"));
            }

            string target = FileSystemHelper.SafeChildPath(destination, Path.GetRelativePath(source, path));
            if (Directory.Exists(path))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CopyFile(path, target);
            }
        }
    }

    internal static bool SameApplication(string source, string destination, bool bundle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!bundle)
        {
            return SameFile(source, destination);
        }
        if (!Directory.Exists(destination))
        {
            return false;
        }
        string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        if (files.Length != Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length)
        {
            return false;
        }
        foreach (string file in files)
        {
            token.ThrowIfCancellationRequested();
            if (!SameFile(file, FileSystemHelper.SafeChildPath(destination, Path.GetRelativePath(source, file))))
            {
                return false;
            }
        }
        return true;

        static bool SameFile(string source, string target)
        {
            if (!File.Exists(target) || new FileInfo(source).Length != new FileInfo(target).Length)
            {
                return false;
            }
            using FileStream original = File.OpenRead(source);
            using FileStream installed = File.OpenRead(target);
            return SHA256.HashData(original).SequenceEqual(SHA256.HashData(installed));
        }
    }

    private static void CopyFile(string source, string target)
    {
        File.Copy(source, target);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(target, File.GetUnixFileMode(source) | UnixFileMode.UserRead);
        }
    }

    [SupportedOSPlatform("windows")]
    internal static void CreateWindowsShortcut(string path, string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
                         ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable.");
        object shell = Activator.CreateInstance(shellType)
                       ?? throw new InvalidOperationException("Windows Script Host could not be started.");
        object? shortcut = null;
        try
        {
            dynamic link = shortcut = ((dynamic)shell).CreateShortcut(path);
            link.TargetPath = executable;
            link.WorkingDirectory = Path.GetDirectoryName(executable);
            link.IconLocation = executable + ",0";
            link.Description = "Prowl";
            link.Save();
        }
        finally
        {
            if (shortcut != null)
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string DesktopDirectory()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return desktop.Length > 0 ? desktop : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
    }

    internal static string DesktopEntry(string executable, string icon) => $"""
                                                                            [Desktop Entry]
                                                                            Type=Application
                                                                            Name=Prowl
                                                                            Exec={QuoteDesktopExecutable(executable)}
                                                                            Icon={icon.Replace("\\", "\\\\")}
                                                                            Terminal=false
                                                                            Categories=Development;
                                                                            """.ReplaceLineEndings("\n") + "\n";

    internal static string QuoteDesktopExecutable(string path) => "\"" + path
        .Replace("\\", "\\\\\\\\")
        .Replace("\"", "\\\\\\\"")
        .Replace("$", "\\\\$")
        .Replace("`", "\\\\`")
        .Replace("%", "%%") + "\"";

    private static void WriteDesktopEntry(string path, string entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, entry);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
