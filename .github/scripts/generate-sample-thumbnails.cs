#:package Prowl.Aperture@3.6.7

// Manually capture samples after 2.5 seconds without the HUD; update images changed by at least 25%.
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Aperture;

const double MinimumDifference = 0.25;

string repository = RepositoryDirectory();
string outputArgument = Path.Combine(repository, "Prowl.Launcher", "Resources", "Samples");
string engine = Path.Combine(repository, "Engine");
string platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
string runtime = $"{platform}-{architecture}";

for (int index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--engine" when index + 1 < args.Length:
            engine = Path.GetFullPath(args[++index]);
            break;

        case "--output" when index + 1 < args.Length:
            outputArgument = args[++index];
            break;

        case "--runtime" when index + 1 < args.Length:
            runtime = args[++index];
            break;

        case "--help":
        case "-h":
            Console.WriteLine("Usage: dotnet run --file .github/scripts/generate-sample-thumbnails.cs [-- --output <directory> --runtime <platform> --engine <checkout>]");
            return 0;

        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[index]}");
            return 1;
    }
}

string output = Path.GetFullPath(outputArgument);
using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, signal) =>
{
    signal.Cancel = true;
    cancellation.Cancel();
};

DirectoryInfo? work = null;

try
{
    Directory.CreateDirectory(output);

    work = Directory.CreateTempSubdirectory("prowl-sample-thumbnails-");
    string samples = Path.Combine(work.FullName, "samples");
    string host = Path.Combine(samples, "Host");
    string captures = Path.Combine(work.FullName, "captures");
    Directory.CreateDirectory(captures);

    await RunAsync(600, "msbuild", "Samples/Samples.proj", "-t:Build",
        "-p:Configuration=Release", $"-p:RuntimeIdentifier={runtime}", $"-p:SampleOutputRoot={samples}/", $"-p:EngineDirectory={engine}");

    string source = Path.Combine(engine, "Samples", "Runtime");
    foreach (string project in Directory.EnumerateDirectories(source)
        .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj"))
        .Order(StringComparer.Ordinal))
    {
        string sample = Path.GetFileNameWithoutExtension(project);
        string thumbnail = Path.Combine(captures, sample + ".png");

        Console.WriteLine($"Capturing {sample}");
        await RunAsync(90, Path.Combine(host, "Prowl.SampleHost.dll"), "--run-sample",
            Path.Combine(samples, "Built", sample, sample + ".dll"), "--sample-preview", thumbnail);

        if (!File.Exists(thumbnail) || new FileInfo(thumbnail).Length == 0)
            throw new InvalidOperationException($"Missing thumbnail for {sample}");
    }

    string[] images = Directory.GetFiles(captures, "*.png");
    if (images.Length == 0)
        throw new InvalidOperationException("No sample thumbnails captured.");

    foreach (string image in images.Order(StringComparer.Ordinal))
    {
        string target = Path.Combine(output, Path.GetFileName(image));
        double difference = File.Exists(target) ? Difference(target, image) : 1;

        Console.WriteLine($"{Path.GetFileName(image)}: {difference:P1} changed");
        if (difference >= MinimumDifference)
            File.Copy(image, target, overwrite: true);
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
finally
{
    try
    {
        work?.Delete(recursive: true);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Could not remove temporary build files: {exception.Message}");
    }
}

async Task RunAsync(int timeoutSeconds, params string[] arguments)
{
    cancellation.Token.ThrowIfCancellationRequested();

    ProcessStartInfo start = new("dotnet")
    {
        WorkingDirectory = repository,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (string argument in arguments)
        start.ArgumentList.Add(argument);

    using Process process = Process.Start(start)
        ?? throw new InvalidOperationException("Could not start dotnet.");

    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
    timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

    try
    {
        await process.WaitForExitAsync(timeout.Token);
    }
    catch (OperationCanceledException)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);

        await process.WaitForExitAsync();
        cancellation.Token.ThrowIfCancellationRequested();
        throw new TimeoutException($"dotnet {arguments[0]} timed out after {timeoutSeconds} seconds.");
    }

    if (process.ExitCode != 0)
        throw new InvalidOperationException($"dotnet {arguments[0]} failed with exit code {process.ExitCode}.");
}

static string RepositoryDirectory([CallerFilePath] string script = "") =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(script)!, "..", ".."));

static double Difference(string previous, string current)
{
    const int PixelTolerance = 16;

    DecodeOptions options = new() { TargetPixelFormat = PixelFormat.Rgba8 };
    using Image before = Image.Load(previous, options);
    using Image after = Image.Load(current, options);

    if (before.Width != after.Width || before.Height != after.Height)
        return 1;

    ReadOnlySpan<byte> left = before.Pixels;
    ReadOnlySpan<byte> right = after.Pixels;
    int changed = 0;
    for (int pixel = 0; pixel < left.Length; pixel += 4)
    {
        // Ignore minor renderer noise; count a pixel if any RGB channel differs by 16/255.
        if (Math.Abs(left[pixel] - right[pixel]) >= PixelTolerance
            || Math.Abs(left[pixel + 1] - right[pixel + 1]) >= PixelTolerance
            || Math.Abs(left[pixel + 2] - right[pixel + 2]) >= PixelTolerance)
            changed++;
    }

    return changed / (double)(before.Width * before.Height);
}
