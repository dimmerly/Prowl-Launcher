using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

using Prowl.Rosetta;

namespace Prowl.Launcher;

sealed class LauncherInstallationService(LauncherStore store)
{
    internal static bool ShouldOffer(LauncherStore store) => !store.Settings.InstallationPromptHandled
                                                             && typeof( Launcher ).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                                                                 .Any(attribute => attribute.Key == "SelfInstall" && attribute.Value == "true")
                                                             && SourcePath() != null;

    internal static string InstallRoot(LauncherStore store)
    {
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Prowl Launcher");
        }

        return Path.Combine(store.Home, "Application");
    }

    internal static bool IsRunningInstallation(string executable) =>
        Environment.ProcessPath is {} processPath && LauncherStore.PathsEqual(executable, processPath)
        || SourcePath() is {} source && LauncherStore.PathsEqual(executable, source);

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
        string version = typeof( Launcher ).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        string root = InstallRoot(store);
        string fileName = OperatingSystem.IsWindows() ? "Prowl.Launcher.exe" : "Prowl Launcher.AppImage";
        string relative = OperatingSystem.IsMacOS() ? "Prowl Launcher.app"
            : OperatingSystem.IsWindows() ? fileName : Path.Combine(version, fileName);
        string destination = LauncherStore.SafeChildPath(root, relative);
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

    internal void MigrateWindowsInstallation(string? installationRoot = null, string[]? shortcutPaths = null)
    {
        if (!OperatingSystem.IsWindows() || store.Settings.InstalledLauncherExecutable is not {} previous)
        {
            return;
        }
        string root = installationRoot ?? InstallRoot(store);
        string stable = Path.Combine(root, "Prowl.Launcher.exe");
        if (LauncherStore.PathsEqual(previous, stable) || !File.Exists(previous)
            || !string.Equals(Path.GetFileName(previous), "Prowl.Launcher.exe", StringComparison.OrdinalIgnoreCase)
            || Path.GetDirectoryName(Path.GetDirectoryName(previous)) is not {} parent
            || !LauncherStore.PathsEqual(parent, root))
        {
            return;
        }

        if (!File.Exists(stable))
        {
            try { CopyApplication(previous, stable, false, default); }
            catch (IOException) when (File.Exists(stable)) { } // Another launcher completed the same migration.
        }
        string[] shortcuts = shortcutPaths ??
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Prowl Launcher.lnk"),
            Path.Combine(DesktopDirectory(), "Prowl Launcher.lnk")
        ];
        foreach (string shortcut in shortcuts)
        {
            if (File.Exists(shortcut)) CreateWindowsShortcut(shortcut, stable, previous);
        }
        store.Settings.InstalledLauncherExecutable = stable;
        store.Save();
    }

    private static void RegisterApplication(string root, string destination, string executable, bool desktopShortcut)
    {
        // Register the app even when the optional desktop shortcut is disabled.
        if (OperatingSystem.IsWindows())
        {
            RegisterWindowsApplication(executable, desktopShortcut);
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
    private static void RegisterWindowsApplication(string executable, bool desktopShortcut)
    {
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        CreateWindowsShortcut(Path.Combine(programs, "Prowl Launcher.lnk"), executable);

        if (desktopShortcut)
        {
            CreateWindowsShortcut(Path.Combine(DesktopDirectory(), "Prowl Launcher.lnk"), executable);
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

        string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        string entry = DesktopEntry(executable, icon);
        WriteDesktopEntry(Path.Combine(data, "applications", "com.prowlengine.launcher.desktop"), entry);

        if (desktopShortcut)
        {
            WriteDesktopEntry(Path.Combine(DesktopDirectory(), "Prowl Launcher.desktop"), entry);
        }
    }

    private static void CreateMacShortcut(string destination)
    {
        string shortcut = Path.Combine(DesktopDirectory(), "Prowl Launcher.app");
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
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(Loc.Get("launcher.errors.archive_link"));
            }

            string target = LauncherStore.SafeChildPath(destination, Path.GetRelativePath(source, path));
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
            if (!SameFile(file, LauncherStore.SafeChildPath(destination, Path.GetRelativePath(source, file))))
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
    internal static void CreateWindowsShortcut(string path, string executable, string? expectedTarget = null)
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
            if (expectedTarget != null && !LauncherStore.PathsEqual((string)link.TargetPath, expectedTarget))
            {
                return;
            }
            link.TargetPath = executable;
            link.WorkingDirectory = Path.GetDirectoryName(executable);
            link.IconLocation = executable + ",0";
            link.Description = "Prowl Launcher";
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
                                                                            Name=Prowl Launcher
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
