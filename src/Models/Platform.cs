using Prowl.Rosetta;

using System.Runtime.InteropServices;

namespace Prowl.Launcher;

public static class Platform
{
    public static string Identifier
    {
        get
        {
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => throw new PlatformNotSupportedException(Loc.Get("launcher.errors.unsupported_architecture"))
            };
            return $"{os}-{arch}";
        }
    }

    public static string DefaultVersionsPath => Path.Combine(
        Environment.GetFolderPath(Constants.Storage.HomeBaseFolder),
        Constants.Storage.ApplicationFolderName,
        "Versions");

    public static string DefaultHome => Path.Combine(
        Environment.GetFolderPath(Constants.Storage.HomeBaseFolder),
        Constants.Storage.ApplicationFolderName,
        Constants.Storage.LauncherFolderName);
}
