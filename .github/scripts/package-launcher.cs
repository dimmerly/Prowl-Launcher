using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

if (args.Length != 5)
{
    Console.Error.WriteLine("Usage: package-launcher.cs <publish-directory> <update-directory> <platform> <version> <archive>");
    return 1;
}

string publish = Path.GetFullPath(args[0]);
string updatePublish = Path.GetFullPath(args[1]);
string platform = args[2];
string version = args[3];
string archive = Path.GetFullPath(args[4]);
string repository = RepositoryDirectory();
string executable = platform.StartsWith("win-") ? "Prowl.Launcher.exe" : "Prowl.Launcher";
if (!File.Exists(Path.Combine(publish, executable)))
    throw new FileNotFoundException("The published launcher is missing.");
if (!File.Exists(Path.Combine(updatePublish, executable)) || !File.Exists(Path.Combine(updatePublish, "Prowl.Launcher.dll")))
    throw new InvalidOperationException("Update archives require a separate PublishSingleFile=false publish.");
if (platform.StartsWith("win-") && File.Exists(Path.Combine(publish, "Prowl.Launcher.dll")))
    throw new InvalidOperationException("Windows downloads require PublishSingleFile=true and IncludeNativeLibrariesForSelfExtract=true.");

if (!OperatingSystem.IsWindows() && !platform.StartsWith("win-"))
    File.SetUnixFileMode(Path.Combine(publish, executable), File.GetUnixFileMode(Path.Combine(publish, executable)) | UnixFileMode.UserExecute);

Directory.CreateDirectory(Path.GetDirectoryName(archive)!);

if (platform.StartsWith("win-"))
{
    WriteUpdateArchive(updatePublish, archive);
    PackageWindows();
}
else if (platform.StartsWith("osx-"))
{
    PackageMac();
}
else if (platform.StartsWith("linux-"))
{
    WriteUpdateArchive(updatePublish, archive);
    PackageLinux();
}
else
{
    WriteUpdateArchive(updatePublish, archive);
}

return 0;

void PackageWindows()
{
    string download = DownloadPath(".exe");
    File.Copy(Path.Combine(publish, executable), download);
    Console.WriteLine(download);
}

string CreateMacBundle(string directory)
{
    string bundle = Path.Combine(Path.GetDirectoryName(directory)!, "Prowl Launcher.app");
    string contents = Path.Combine(bundle, "Contents");
    Directory.CreateDirectory(contents);
    Directory.Move(directory, Path.Combine(contents, "MacOS"));
    string numericVersion = version.Split('+')[0].Split('-')[0];
    XDocument bundleManifest = new(new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
        new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
            Pair("CFBundleName", "Prowl Launcher"),
            Pair("CFBundleDisplayName", "Prowl Launcher"),
            Pair("CFBundleExecutable", executable),
            Pair("CFBundleIdentifier", "com.prowlengine.launcher"),
            Pair("CFBundlePackageType", "APPL"),
            Pair("CFBundleVersion", numericVersion),
            Pair("CFBundleShortVersionString", numericVersion),
            new XElement("key", "NSHighResolutionCapable"),
            new XElement("true"))));
    bundleManifest.Save(Path.Combine(contents, "Info.plist"));

    if (!OperatingSystem.IsMacOS())
        throw new PlatformNotSupportedException("Creating a DMG requires a macOS runner.");
    string resources = Path.Combine(bundle, "Contents", "Resources");
    Directory.CreateDirectory(resources);
    string iconSet = Path.Combine(Path.GetDirectoryName(bundle)!, "prowl.iconset");
    Directory.CreateDirectory(iconSet);
    foreach (int size in new[] { 16, 32, 128, 256, 512 })
        foreach (int scale in new[] { 1, 2 })
            Run("sips", "-z", (size * scale).ToString(), (size * scale).ToString(),
                Path.Combine(repository, "src", "Resources", "prowl.png"), "--out",
                Path.Combine(iconSet, $"icon_{size}x{size}{(scale == 2 ? "@2x" : "")}.png"));
    Run("iconutil", "-c", "icns", iconSet, "-o", Path.Combine(resources, "prowl.icns"));
    Directory.Delete(iconSet, true);
    XDocument plist = XDocument.Load(Path.Combine(bundle, "Contents", "Info.plist"));
    plist.Root!.Element("dict")!.Add(Pair("CFBundleIconFile", "prowl.icns"));
    plist.Save(Path.Combine(bundle, "Contents", "Info.plist"));
    // Local ad-hoc signatures only: no Developer ID, certificate, or notarization.
    Run("codesign", "--force", "--deep", "--sign", "-", "--preserve-metadata=entitlements", bundle);
    Run("codesign", "--verify", "--deep", "--strict", bundle);
    return bundle;
}

void PackageMac()
{
    publish = CreateMacBundle(publish);
    WriteUpdateArchive(CreateMacBundle(updatePublish), archive);

    string staging = Path.Combine(Path.GetDirectoryName(publish)!, "dmg");
    Directory.CreateDirectory(staging);
    Directory.Move(publish, Path.Combine(staging, "Prowl Launcher.app"));
    Directory.CreateSymbolicLink(Path.Combine(staging, "Applications"), "/Applications");
    string download = DownloadPath(".dmg");
    // Size the app itself, excluding the Applications link, and leave room for filesystem overhead.
    long bundleBytes = new DirectoryInfo(Path.Combine(staging, "Prowl Launcher.app"))
        .EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
    long imageSizeMb = 64 + (long)Math.Ceiling(bundleBytes / 1048576d * 1.25);
    Console.WriteLine($"Creating a {imageSizeMb} MiB disk image for {bundleBytes / 1048576d:F1} MiB of app files.");
    Run("hdiutil", "create", "-volname", "Prowl Launcher", "-srcfolder", staging,
        "-fs", "HFS+", "-size", $"{imageSizeMb}m", "-format", "UDZO", download);
    Console.WriteLine(download);
}

void PackageLinux()
{
    if (!OperatingSystem.IsLinux())
        throw new PlatformNotSupportedException("Creating an AppImage requires a Linux runner.");
    string tool = Environment.GetEnvironmentVariable("APPIMAGETOOL")
        ?? throw new InvalidOperationException("Set APPIMAGETOOL to the verified appimagetool binary.");
    string appDir = Path.Combine(Path.GetDirectoryName(publish)!, "Prowl Launcher.AppDir");
    Directory.CreateDirectory(Path.Combine(appDir, "usr"));
    Directory.Move(publish, Path.Combine(appDir, "usr", "bin"));
    File.Copy(Path.Combine(repository, "src", "Resources", "prowl.png"), Path.Combine(appDir, "prowl.png"));
    File.WriteAllText(Path.Combine(appDir, "prowl.desktop"),
        """
        [Desktop Entry]
        Type=Application
        Name=Prowl Launcher
        Exec=Prowl.Launcher
        Icon=prowl
        Terminal=false
        Categories=Development;
        """.ReplaceLineEndings("\n") + "\n");
    string appRun = Path.Combine(appDir, "AppRun");
    File.WriteAllText(appRun,
        """
        #!/bin/sh
        app_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
        export LD_LIBRARY_PATH="$app_dir/usr/bin:${LD_LIBRARY_PATH:-}"
        exec "$app_dir/usr/bin/Prowl.Launcher" "$@"
        """.ReplaceLineEndings("\n") + "\n");
    File.SetUnixFileMode(appRun,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

    // Reuse the runtime shipped inside this pinned tool rather than fetching a mutable runtime.
    string runtime = Path.Combine(Path.GetDirectoryName(appDir)!, "appimage-runtime");
    ExtractAppImageRuntime(tool, runtime);

    Environment.SetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN", "1");
    Environment.SetEnvironmentVariable("ARCH", platform == "linux-arm64" ? "aarch64" : "x86_64");
    string download = DownloadPath(".AppImage");
    Run(tool, "--runtime-file", runtime, appDir, download);
    Console.WriteLine(download);
}

string DownloadPath(string extension) => Path.ChangeExtension(archive, extension);

static void ExtractAppImageRuntime(string tool, string runtime)
{
    long offset = long.Parse(Run(tool, "--appimage-offset").Trim());
    using (FileStream input = File.OpenRead(tool))
    using (FileStream output = File.Create(runtime))
    {
        byte[] buffer = new byte[81920];
        while (offset > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(offset, buffer.Length));
            if (read == 0)
                throw new EndOfStreamException("Invalid AppImage runtime offset.");
            output.Write(buffer, 0, read);
            offset -= read;
        }
    }
}

static void WriteUpdateArchive(string publish, string archive)
{
    ZipFile.CreateFromDirectory(publish, archive, CompressionLevel.Optimal, includeBaseDirectory: true);
    Console.WriteLine(archive);
}

static XElement[] Pair(string key, string value) => [new("key", key), new("string", value)];

static string RepositoryDirectory([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../.."));

static string Run(string executable, params string[] arguments)
{
    ProcessStartInfo start = new(executable) { UseShellExecute = false, RedirectStandardOutput = true };
    foreach (string argument in arguments)
        start.ArgumentList.Add(argument);
    using Process process = Process.Start(start)
        ?? throw new InvalidOperationException($"Could not start {executable}.");
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{executable} failed with exit code {process.ExitCode}.");
    return output;
}
