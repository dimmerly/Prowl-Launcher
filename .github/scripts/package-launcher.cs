using System.IO.Compression;
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
string executable = platform.StartsWith("win-") ? "Prowl.Launcher.exe" : "Prowl.Launcher";
if (!File.Exists(Path.Combine(publish, executable)))
    throw new FileNotFoundException("The published launcher is missing.");

if (!OperatingSystem.IsWindows() && !platform.StartsWith("win-"))
    File.SetUnixFileMode(Path.Combine(publish, executable), File.GetUnixFileMode(Path.Combine(publish, executable)) | UnixFileMode.UserExecute);

if (platform.StartsWith("osx-"))
{
    string bundle = Path.Combine(Path.GetDirectoryName(publish)!, "Prowl Launcher.app");
    string contents = Path.Combine(bundle, "Contents");
    Directory.CreateDirectory(contents);
    Directory.Move(publish, Path.Combine(contents, "MacOS"));
    string numericVersion = version.Split('+')[0].Split('-')[0];
    XDocument plist = new(new XDeclaration("1.0", "UTF-8", null),
        new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
        new XElement("plist", new XAttribute("version", "1.0"), new XElement("dict",
            Pair("CFBundleName", "Prowl Launcher"), Pair("CFBundleDisplayName", "Prowl Launcher"),
            Pair("CFBundleExecutable", executable), Pair("CFBundleIdentifier", "com.prowlengine.launcher"),
            Pair("CFBundlePackageType", "APPL"), Pair("CFBundleVersion", numericVersion),
            Pair("CFBundleShortVersionString", numericVersion), new XElement("key", "NSHighResolutionCapable"), new XElement("true"))));
    plist.Save(Path.Combine(contents, "Info.plist"));
    publish = bundle;
}

Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
ZipFile.CreateFromDirectory(publish, archive, CompressionLevel.Optimal, includeBaseDirectory: true);
Console.WriteLine(archive);
return 0;

static XElement[] Pair(string key, string value) => [new("key", key), new("string", value)];
