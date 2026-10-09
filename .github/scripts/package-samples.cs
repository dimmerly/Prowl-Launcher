using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

const string Usage = "Usage: package-samples.cs <engine-checkout> <sample-output> <platform> <archive>";

if (args is ["--help"])
{
    Console.WriteLine(Usage);
    return;
}

if (args.Length != 4)
{
    throw new ArgumentException(Usage);
}

string engine = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
string platform = args[2];
string archive = Path.GetFullPath(args[3]);

if (!File.Exists(Path.Combine(engine, "Prowl.Runtime", "Prowl.Runtime.csproj")))
{
    throw new DirectoryNotFoundException("Check out Prowl at EngineRevision.txt before building samples.");
}

string host = Path.Combine(output, "Host");
string payload = Path.Combine(output, "Payload");
string hostProject = Path.Combine(output, "HostProject");

string[] sampleIds = BuildSamples(engine, output, payload, platform);
WriteHostProject(engine, hostProject);
PublishHost(hostProject, host, platform);
CreateArchive(output, host, payload, sampleIds, archive);

Console.WriteLine($"Packaged {sampleIds.Length} samples and their runtime for {platform}: {archive}");

static string[] BuildSamples(string engine, string output, string payload, string platform)
{
    string source = Path.Combine(engine, "Samples", "Runtime");
    string[] projects = Directory.EnumerateDirectories(source)
        .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj"))
        .Order(StringComparer.Ordinal)
        .ToArray();

    if (projects.Length == 0)
    {
        throw new InvalidDataException("No sample projects were found.");
    }

    List<string> ids = [];
    foreach (string project in projects)
    {
        string id = Path.GetFileNameWithoutExtension(project);
        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_-]*\z"))
        {
            throw new InvalidDataException("Invalid sample name: " + id);
        }

        string built = Path.Combine(output, "Built", id);
        RunDotnet(
            "build", project,
            "-c", "Release",
            "-r", platform,
            "--self-contained", "false",
            "-p:AppendTargetFrameworkToOutputPath=false",
            "-p:AppendRuntimeIdentifierToOutputPath=false",
            "-p:OutputPath=" + built + Path.DirectorySeparatorChar);

        string assembly = Path.Combine(built, id + ".dll");
        if (AssemblyName.GetAssemblyName(assembly).Name != id)
        {
            throw new InvalidDataException("Unexpected sample assembly: " + assembly);
        }

        string destination = Path.Combine(payload, id);
        Directory.CreateDirectory(destination);
        File.Copy(assembly, Path.Combine(destination, id + ".dll"), overwrite: true);

        string assets = Path.Combine(Path.GetDirectoryName(project)!, "Assets");
        if (Directory.Exists(assets))
        {
            CopyDirectory(assets, Path.Combine(destination, "Assets"));
        }

        ids.Add(id);
    }

    return ids.ToArray();
}

static void PublishHost(string project, string output, string platform)
{
    RunDotnet(
        "publish", Path.Combine(project, "Prowl.SampleHost.csproj"),
        "-c", "Release",
        "-r", platform,
        "--self-contained", "true",
        "-o", output);

    if (platform == "linux-arm64")
    {
        File.Copy("/usr/lib/aarch64-linux-gnu/libglfw.so.3", Path.Combine(output, "libglfw.so.3"), overwrite: true);
    }

    string executable = platform.StartsWith("win-") ? "Prowl.SampleHost.exe" : "Prowl.SampleHost";
    foreach (string file in new[] { executable, "Prowl.SampleHost.dll", "Prowl.SampleHost.runtimeconfig.json" })
    {
        if (!File.Exists(Path.Combine(output, file)))
        {
            throw new FileNotFoundException("Missing sample host file: " + file);
        }
    }
}

static void CreateArchive(string output, string host, string payload, string[] ids, string archive)
{
    JsonArray entries = [];
    foreach (string id in ids)
    {
        entries.Add((JsonNode?)JsonValue.Create(id));
    }

    string catalog = entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(output, "samples.json"), catalog);
    Directory.CreateDirectory(Path.GetDirectoryName(archive)!);

    // Build a new archive before replacing a previous successful output.
    string temporary = archive + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        using (ZipArchive zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            AddDirectory(zip, host, "Host/");
            foreach (string id in ids)
            {
                AddDirectory(zip, Path.Combine(payload, id), "Samples/" + id + "/");
            }

            using StreamWriter manifest = new(zip.CreateEntry("samples.json").Open());
            manifest.Write(catalog);
        }

        File.Move(temporary, archive, overwrite: true);
    }
    finally
    {
        File.Delete(temporary);
    }
}

static void AddDirectory(ZipArchive zip, string source, string prefix)
{
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string entry = prefix + Path.GetRelativePath(source, file).Replace('\\', '/');
        zip.CreateEntryFromFile(file, entry);
    }
}

static void CopyDirectory(string source, string destination)
{
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
    }
}

static void RunDotnet(params string[] arguments)
{
    ProcessStartInfo start = new("dotnet") { UseShellExecute = false };
    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using Process process = Process.Start(start)!;
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException("Sample build failed: " + string.Join(' ', arguments));
    }
}

static void WriteHostProject(string engine, string hostProject)
{
    Directory.CreateDirectory(hostProject);
    File.WriteAllText(Path.Combine(hostProject, "Prowl.SampleHost.csproj"), $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="{SecurityElement.Escape(Path.Combine(engine, "Prowl.Runtime", "Prowl.Runtime.csproj"))}" />
          </ItemGroup>
        </Project>
        """);
    File.WriteAllText(Path.Combine(hostProject, "Program.cs"), """
        using Prowl.Samples;

        if (args.Length < 2 || args[0] != "--run-sample")
        {
            Console.Error.WriteLine("Usage: Prowl.SampleHost --run-sample <assembly> [--sample-preview <image>]");
            return 1;
        }
        try
        {
            SampleRunner.Run(args[1], args);
            return Environment.ExitCode;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        """);
    File.WriteAllText(Path.Combine(hostProject, "SampleRunner.cs"), """
        using System.Reflection;
        using System.Runtime.InteropServices;
        using System.Runtime.Loader;

        namespace Prowl.Samples;

        internal static class SampleRunner
        {
            public static void Run(string assemblyPath, string[] args)
            {
                int preview = Array.IndexOf(args, "--sample-preview");
                if (preview >= 0 && preview + 1 < args.Length)
                    SamplePreviewHelper.Capture(args[preview + 1]);
                string launcherDirectory = AppContext.BaseDirectory;
                // Vendored audio libraries live under runtimes rather than the application root.
                NativeLibrary.SetDllImportResolver(typeof(Prowl.Runtime.Game).Assembly, (name, _, _) =>
                {
                    if (name != "miniaudioex")
                        return IntPtr.Zero;
                    string file = OperatingSystem.IsWindows() ? "miniaudioex.dll"
                        : OperatingSystem.IsMacOS() ? "libminiaudioex.dylib" : "libminiaudioex.so";
                    string path = Path.Combine(launcherDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", file);
                    return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
                });
                Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
                MethodInfo entry = assembly.EntryPoint ?? throw new InvalidDataException("Missing sample entry point.");
                object? result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : [args]);
                if (result is Task task)
                    task.GetAwaiter().GetResult();
                if (result is int exitCode)
                    Environment.ExitCode = exitCode;
            }
        }
        """);
    File.WriteAllText(Path.Combine(hostProject, "SamplePreviewHelper.cs"), """
        using System.Diagnostics;
        using Prowl.Aperture;
        using Prowl.Runtime;
        using Prowl.Runtime.Resources;

        namespace Prowl.Samples;

        internal static class SamplePreviewHelper
        {
            public static void Capture(string path)
            {
                long? started = null;
                Window.PostRender += _ =>
                {
                    started ??= Stopwatch.GetTimestamp();
                    if (Stopwatch.GetElapsedTime(started.Value).TotalSeconds < 2.5)
                        return;
                    using Texture2D texture = Graphics.Screenshot();
                    int width = (int)texture.Width, height = (int)texture.Height;
                    byte[] pixels = new byte[width * height * 4];
                    texture.GetData<byte>(pixels);
                    byte[] flipped = new byte[pixels.Length];
                    for (int y = 0; y < height; y++)
                        Array.Copy(pixels, y * width * 4, flipped, (height - 1 - y) * width * 4, width * 4);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                    using Image image = Image.FromPixels(flipped, width, height, PixelFormat.Rgba8);
                    image.Save(path);
                    Window.Stop(force: true);
                };
            }
        }
        """);
}
