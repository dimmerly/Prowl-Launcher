using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: package-launcher.cs <publish-directory> <platform> <version> <archive>");
    return 1;
}

string publish = Path.GetFullPath(args[0]);
string platform = args[1];
string version = args[2];
string archive = Path.GetFullPath(args[3]);
string repository = RepositoryDirectory();
string executable = platform.StartsWith("win-") ? "Prowl.Launcher.exe" : "Prowl.Launcher";
if (!File.Exists(Path.Combine(publish, executable)))
    throw new FileNotFoundException("The published launcher is missing.");
if (platform.StartsWith("win-") && File.Exists(Path.Combine(publish, "Prowl.Launcher.dll")))
    throw new InvalidOperationException("Windows downloads require PublishSingleFile=true and IncludeNativeLibrariesForSelfExtract=true.");

if (!OperatingSystem.IsWindows() && !platform.StartsWith("win-"))
    File.SetUnixFileMode(Path.Combine(publish, executable), File.GetUnixFileMode(Path.Combine(publish, executable)) | UnixFileMode.UserExecute);

Directory.CreateDirectory(Path.GetDirectoryName(archive)!);

if (platform.StartsWith("win-"))
{
    WriteUpdateArchive(publish, archive);
    PackageWindows();
}
else if (platform.StartsWith("osx-"))
{
    PackageMac();
}
else if (platform.StartsWith("linux-"))
{
    WriteUpdateArchive(publish, archive);
    PackageLinux();
}
else
{
    WriteUpdateArchive(publish, archive);
}

return 0;

void PackageWindows()
{
    string download = Path.ChangeExtension(archive, ".exe");
    File.Copy(Path.Combine(publish, executable), download);
    Console.WriteLine(download);
}

void PackageMac()
{
    string bundle = Path.Combine(Path.GetDirectoryName(publish)!, "Prowl Launcher.app");
    string contents = Path.Combine(bundle, "Contents");
    Directory.CreateDirectory(contents);
    Directory.Move(publish, Path.Combine(contents, "MacOS"));
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
    publish = bundle;

    if (!OperatingSystem.IsMacOS())
        throw new PlatformNotSupportedException("Creating a DMG requires a macOS runner.");
    string resources = Path.Combine(publish, "Contents", "Resources");
    Directory.CreateDirectory(resources);
    string iconSet = Path.Combine(Path.GetDirectoryName(publish)!, "prowl.iconset");
    Directory.CreateDirectory(iconSet);
    foreach (int size in new[] { 16, 32, 128, 256, 512 })
        foreach (int scale in new[] { 1, 2 })
            Run("sips", "-z", (size * scale).ToString(), (size * scale).ToString(),
                Path.Combine(repository, "Prowl.Launcher", "Resources", "prowl.png"), "--out",
                Path.Combine(iconSet, $"icon_{size}x{size}{(scale == 2 ? "@2x" : "")}.png"));
    Run("iconutil", "-c", "icns", iconSet, "-o", Path.Combine(resources, "prowl.icns"));
    Directory.Delete(iconSet, true);
    XDocument plist = XDocument.Load(Path.Combine(publish, "Contents", "Info.plist"));
    plist.Root!.Element("dict")!.Add(Pair("CFBundleIconFile", "prowl.icns"));
    plist.Save(Path.Combine(publish, "Contents", "Info.plist"));
    // Local ad-hoc signatures only: no Developer ID, certificate, or notarization.
    Run("codesign", "--force", "--deep", "--sign", "-", "--preserve-metadata=entitlements", publish);
    Run("codesign", "--verify", "--deep", "--strict", publish);
    // The updater needs the same bundle, including its icon.
    WriteUpdateArchive(publish, archive);

    string staging = Path.Combine(Path.GetDirectoryName(publish)!, "dmg");
    Directory.CreateDirectory(staging);
    Directory.Move(publish, Path.Combine(staging, "Prowl Launcher.app"));
    Directory.CreateSymbolicLink(Path.Combine(staging, "Applications"), "/Applications");
    string download = Path.ChangeExtension(archive, ".dmg");
    Run("hdiutil", "create", "-volname", "Prowl Launcher", "-srcfolder", staging, "-format", "UDZO", download);
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
    File.Copy(Path.Combine(repository, "Prowl.Launcher", "Resources", "prowl.png"), Path.Combine(appDir, "prowl.png"));
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
    string download = Path.ChangeExtension(archive, ".AppImage");
    Run(tool, "--runtime-file", runtime, appDir, download);
    Console.WriteLine(download);
}

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
    using Process process = Process.Start(start)!;
    string output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{executable} failed with exit code {process.ExitCode}.");
    return output;
}
