using System.IO.Compression;
using Prowl.Rosetta;
using System.Diagnostics;

namespace Prowl.Launcher;

internal static class SampleService
{
    public static IReadOnlyList<Sample> Samples
    {
        get;
    } =
        Discover(typeof(Launcher).Assembly.GetManifestResourceNames());

    internal static IReadOnlyList<Sample> Discover(IEnumerable<string> resources) => resources
        .Where(name => name.StartsWith("Prowl.Launcher.Samples.", StringComparison.Ordinal)
            && name.EndsWith(".zip", StringComparison.Ordinal))
        .Select(name => name["Prowl.Launcher.Samples.".Length..^4])
        .Where(id => System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9_.-]*$"))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .Select(id => new Sample(id))
        .ToArray();

    public static async Task RunAsync(Sample sample, string workDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Each running sample owns its output, including any files it keeps open.
        string output = Path.Combine(workDirectory, "sample-" + Guid.NewGuid().ToString("N"));
        string assembly = await ExtractAsync(sample, output, token);
        token.ThrowIfCancellationRequested();
        Process process = Process.Start(SampleHelper.LaunchInfo(assembly))
            ?? throw new InvalidOperationException(Loc.Get("launcher.samples.start_failed"));
        Task<int> completion = MonitorAsync(process, output);
        // Report startup crashes through the operation panel, retaining logs for later failures.
        if (await Task.WhenAny(completion, Task.Delay(1500)) == completion && await completion != 0)
            throw new InvalidOperationException(Loc.Get("launcher.samples.start_failed"));
    }

    private static async Task<int> MonitorAsync(Process process, string output)
    {
        int exitCode;
        using (process)
        {
            await using FileStream stdout = File.Create(Path.Combine(output, "stdout.log"));
            await using FileStream stderr = File.Create(Path.Combine(output, "stderr.log"));
            Task stdoutCopy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
            Task stderrCopy = process.StandardError.BaseStream.CopyToAsync(stderr);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdoutCopy, stderrCopy);
            exitCode = process.ExitCode;
        }
        if (exitCode != 0)
            return exitCode;
        try
        {
            Directory.Delete(output, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A sample may leave another process using its files; keep those files intact.
        }
        return exitCode;
    }

    public static bool IsAvailable(Sample sample) => Samples.Any(known => known.Id == sample.Id);

    public static async Task<string> ExtractAsync(Sample sample, string outputDirectory, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAvailable(sample))
            throw new InvalidOperationException(Loc.Get("launcher.samples.unavailable"));
        using Stream host = typeof(Launcher).Assembly.GetManifestResourceStream("Prowl.Launcher.SampleHost.zip")
            ?? throw new InvalidOperationException(Loc.Get("launcher.samples.unavailable"));
        using ZipArchive hostArchive = new(host);
        await ExtractArchiveAsync(hostArchive, outputDirectory, token);
        using Stream resource = typeof(Launcher).Assembly.GetManifestResourceStream($"Prowl.Launcher.Samples.{sample.Id}.zip")!;
        using ZipArchive archive = new(resource);
        await ExtractArchiveAsync(archive, outputDirectory, token);
        if (!OperatingSystem.IsWindows())
        {
            string executable = Path.Combine(outputDirectory, "Prowl.SampleHost");
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        }
        string assembly = Path.Combine(outputDirectory, sample.Id + ".dll");
        if (!File.Exists(assembly))
            throw new InvalidOperationException(Loc.Get("launcher.samples.unavailable"));
        return assembly;
    }

    private static async Task ExtractArchiveAsync(ZipArchive archive, string outputDirectory, CancellationToken token)
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            string destination = LauncherStore.SafeChildPath(outputDirectory, entry.FullName);
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream source = entry.Open();
            await using FileStream file = File.Create(destination);
            await source.CopyToAsync(file, token);
        }
    }
}
