using System.Runtime.InteropServices;

using Prowl.Rosetta;
namespace Prowl.Launcher;

public static class Platform
{
    /// <summary>The runtime identifier of this process, e.g. <c>win-x64</c>.</summary>
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

    /// <summary>The folder shared by the editor and launcher, e.g. <c>%APPDATA%/Prowl</c>.</summary>
    public static string ProwlDataPath => Path.Combine(
        Environment.GetFolderPath(Constants.Storage.HomeBaseFolder),
        Constants.Storage.ApplicationFolderName);

    public static string DefaultVersionsPath => Path.Combine(ProwlDataPath, "Versions");

    public static string DefaultHome => Path.Combine(ProwlDataPath, Constants.Storage.LauncherFolderName);

    /// <summary>The file name of an apphost built for <paramref name="platform" />, e.g. <c>Prowl.Editor.exe</c>.</summary>
    public static string ExecutableName(string name, string platform) =>
        platform.StartsWith("win-", StringComparison.Ordinal) ? name + ".exe" : name;

    /// <inheritdoc cref="ExecutableName(string, string)" />
    public static string ExecutableName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;
}
