using System.IO.Compression;

using Prowl.Rosetta;

using System.Diagnostics;
using System.Text.Json;

namespace Prowl.Launcher;

sealed class SampleService
{
    private readonly HttpClient http;
    private readonly LauncherStore store;

    public SampleService(HttpClient http, LauncherStore store)
    {
        this.http = http;
        this.store = store;
        try { RecoverInterruptedOperations(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try { File.AppendAllText(Path.Combine(store.Home, "launcher.log"), $"{DateTimeOffset.UtcNow:o} Sample recovery: {error}\n"); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record InstalledBundle(string Repository, string Platform, long ReleaseId, string Tag, string Digest);
    private string? _loadedCache;
    private DateTime _loadedStamp;
    private IReadOnlyList<Sample>? _loadedCatalog;
    private string Repository => GitHubRepositoryHelper.Normalize(store.Settings.LauncherRepository);
    private string CacheRoot => Path.Combine(store.Home, "Samples", GitHubRepositoryHelper.CacheKey(Repository), Platform.Identifier);
    private string ContentPath => Path.Combine(CacheRoot, "content");
    public bool IsCached => InstalledCatalog() != null;
    public IReadOnlyList<Sample> Catalog => InstalledCatalog() ?? Samples;
    public static IReadOnlyList<Sample> Samples
    {
        get;
    } = LoadCatalog();

    private static IReadOnlyList<Sample> LoadCatalog()
    {
        using Stream stream = typeof( Launcher ).Assembly.GetManifestResourceStream("Prowl.Launcher.Samples.catalog.json")!;
        return Discover(JsonSerializer.Deserialize<string[]>(stream) ?? []);
    }

    internal static IReadOnlyList<Sample> Discover(IEnumerable<string> resources) => resources
        .Where(id => System.Text.RegularExpressions.Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_-]*\z"))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .Select(id => new Sample(id))
        .ToArray();

    public async Task RunAsync(Sample sample, IProgress<TransferProgress>? progress, CancellationToken token, bool allowNetwork = true)
    {
        token.ThrowIfCancellationRequested();
        // Each running sample owns its output, including any files it keeps open.
        string output = Path.Combine(store.WorkPath, "sample-" + Guid.NewGuid().ToString("N"));
        string assembly = await ExtractAsync(sample, output, token, progress, allowNetwork);
        token.ThrowIfCancellationRequested();
        Process process = Process.Start(SampleHelper.LaunchInfo(assembly))
                          ?? throw new InvalidOperationException(Loc.Get("launcher.samples.start_failed"));
        Task<int> completion = MonitorAsync(process, output);
        // Report startup crashes through the operation panel, retaining logs for later failures.
        if (await Task.WhenAny(completion, Task.Delay(1500)) == completion && await completion != 0)
        {
            throw new InvalidOperationException(Loc.Get("launcher.samples.start_failed"));
        }
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
        {
            return exitCode;
        }
        try
        {
            Directory.Delete(output, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A sample may leave another process using its files; keep those files intact.
        }
        return exitCode;
    }

    public bool IsAvailable(Sample sample) => Catalog.Any(known => known.Id == sample.Id);

    public async Task<string> ExtractAsync(Sample sample, string outputDirectory, CancellationToken token = default,
        IProgress<TransferProgress>? progress = null, bool allowNetwork = true)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAvailable(sample))
        {
            throw new InvalidOperationException(Loc.Get("launcher.samples.unavailable"));
        }
        await EnsureDownloadedAsync(progress, token, allowNetwork);
        await using FileStream copyLock = await AcquireDownloadLockAsync(Repository, token);
        RecoverCache(Repository, CacheRoot);
        if (!IsAvailable(sample))
        {
            throw new InvalidOperationException(Loc.Get("launcher.samples.unavailable"));
        }
        try
        {
            await CopyDirectoryAsync(Path.Combine(ContentPath, "Host"), outputDirectory, token);
            await CopyDirectoryAsync(Path.Combine(ContentPath, "Samples", sample.Id), outputDirectory, token);
            if (!OperatingSystem.IsWindows())
            {
                string executable = Path.Combine(outputDirectory, "Prowl.SampleHost");
                File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
            }
            return Path.Combine(outputDirectory, sample.Id + ".dll");
        }
        catch
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, true);
            }
            throw;
        }
    }

    private IReadOnlyList<Sample>? InstalledCatalog()
    {
        string metadata = Path.Combine(CacheRoot, "installed.json");
        if (_loadedCache == CacheRoot && File.GetLastWriteTimeUtc(metadata) == _loadedStamp)
        {
            return _loadedCatalog;
        }
        InstalledBundle? installed = LauncherStore.ReadJson<InstalledBundle>(metadata);
        if (installed == null || installed.Repository != Repository || installed.Platform != Platform.Identifier)
        {
            return null;
        }
        try
        {
            _loadedCatalog = ValidateContent(ContentPath);
            _loadedCache = CacheRoot;
            _loadedStamp = File.GetLastWriteTimeUtc(metadata);
            return _loadedCatalog;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<Sample> ValidateContent(string root)
    {
        string[] ids = LauncherStore.ReadJson<string[]>(Path.Combine(root, "samples.json"))
                       ?? throw new InvalidDataException("The sample bundle has no catalog.");
        IReadOnlyList<Sample> samples = Discover(ids);
        if (samples.Count == 0 || samples.Count != ids.Length)
        {
            throw new InvalidDataException("The sample bundle has an invalid catalog.");
        }
        string host = Path.Combine(root, "Host");
        foreach (string file in new[]
            {
                OperatingSystem.IsWindows() ? "Prowl.SampleHost.exe" : "Prowl.SampleHost",
                "Prowl.SampleHost.dll",
                "Prowl.SampleHost.runtimeconfig.json"
            })
        {
            if (!File.Exists(Path.Combine(host, file)))
            {
                throw new InvalidDataException("The sample bundle is missing its runtime.");
            }
        }
        foreach (Sample sample in samples)
        {
            if (!File.Exists(Path.Combine(root, "Samples", sample.Id, sample.Id + ".dll")))
            {
                throw new InvalidDataException("The sample bundle is missing a sample.");
            }
        }
        return samples;
    }

    internal static (EditorRelease Release, ReleaseAsset Asset)? SelectBundle(IEnumerable<EditorRelease> releases, string platform, bool previews)
    {
        foreach (EditorRelease release in releases.Where(r => !r.Draft && r.Id > 0 && (previews || !r.Preview)).OrderByDescending(r => r.Published))
        {
            ReleaseAsset? asset = release.Assets.FirstOrDefault(a => a.Name == $"Prowl-Samples-{platform}.zip");
            if (asset != null)
            {
                return (release, asset);
            }
        }
        return null;
    }

    public async Task<bool> HasUpdateAsync(CancellationToken token = default)
    {
        if (!IsCached)
        {
            return false;
        }
        InstalledBundle installed = LauncherStore.ReadJson<InstalledBundle>(Path.Combine(CacheRoot, "installed.json"))!;
        IReadOnlyList<EditorRelease> releases = await new GitHubReleasesService(http, store, Repository).GetAsync(token);
        (EditorRelease Release, ReleaseAsset Asset)? selected = SelectBundle(releases, Platform.Identifier, store.Settings.LauncherPrereleases);
        return selected != null && selected.Value.Asset.Digest is { Length: 71 }
                                && selected.Value.Asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                                && selected.Value.Release.Id != installed.ReleaseId
                                && !string.Equals(selected.Value.Asset.Digest, installed.Digest, StringComparison.OrdinalIgnoreCase);
    }

    public async Task EnsureDownloadedAsync(IProgress<TransferProgress>? progress = null, CancellationToken token = default,
        bool allowNetwork = true, bool checkForUpdates = false)
    {
        token.ThrowIfCancellationRequested();
        string repository = Repository;
        string cacheRoot = CacheRoot;
        Directory.CreateDirectory(store.Home);
        await using FileStream downloadLock = await AcquireDownloadLockAsync(repository, token);
        RecoverCache(repository, cacheRoot);
        if (IsCached && !checkForUpdates)
        {
            return;
        }
        if (!allowNetwork)
        {
            throw new InvalidOperationException(Loc.Get("launcher.samples.download_required"));
        }
        progress?.Report(new TransferProgress("launcher.samples.downloading"));
        IReadOnlyList<EditorRelease> releases = await new GitHubReleasesService(http, store, repository).GetAsync(token);
        (EditorRelease Release, ReleaseAsset Asset) selected = SelectBundle(releases, Platform.Identifier, store.Settings.LauncherPrereleases)
                                                               ?? throw new InvalidOperationException(Loc.Get("launcher.samples.no_bundle"));
        InstalledBundle? installed = LauncherStore.ReadJson<InstalledBundle>(Path.Combine(cacheRoot, "installed.json"));
        if (IsCached && string.Equals(installed?.Digest, selected.Asset.Digest, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!Uri.TryCreate(selected.Asset.DownloadUrl, UriKind.Absolute, out Uri? source)
            || source.Scheme != "https" || source.Host != "github.com"
            || !source.AbsolutePath.StartsWith($"/{repository}/releases/download/{selected.Release.Tag}/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_download_source"));
        }

        Directory.CreateDirectory(store.WorkPath);
        string staging = Path.Combine(store.WorkPath, "samples-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string archivePath = Path.Combine(staging, "samples.zip");
            await PackageDownloadService.DownloadVerifiedAsync(http, selected.Asset, archivePath, progress, token);
            progress?.Report(new TransferProgress("launcher.samples.extracting"));
            string content = Path.Combine(staging, "content");
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                await ExtractArchiveAsync(archive, content, token);
            }
            ValidateContent(content);
            LauncherStore.WriteJson(Path.Combine(staging, "installed.json"),
                new InstalledBundle(repository, Platform.Identifier, selected.Release.Id, selected.Release.Tag, selected.Asset.Digest!));
            File.Delete(archivePath);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(cacheRoot)!);
            DirectoryReplacementService.Replace(staging, cacheRoot);
            _loadedCache = null;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
    }

    internal void RecoverInterruptedOperations()
    {
        string repository = Repository;
        string cacheRoot = CacheRoot;
        if (!Directory.Exists(Path.GetDirectoryName(cacheRoot))) return;
        FileStream operation;
        // Startup must not wait for another launcher that is downloading a bundle.
        try { operation = new FileStream(DownloadLockPath(repository), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return; }
        using (operation) RecoverCache(repository, cacheRoot);
    }

    private void RecoverCache(string repository, string cacheRoot)
    {
        string parent = Path.GetDirectoryName(cacheRoot)!;
        if (!Directory.Exists(parent)) return;
        DirectoryReplacementService.RejectLink(store.Home);
        DirectoryReplacementService.RejectLink(Path.Combine(store.Home, "Samples"));
        DirectoryReplacementService.RejectLink(parent);
        DirectoryReplacementService.Recover(cacheRoot, path => IsValidCache(path, repository));

        // Restore backups made by older launcher versions as well.
        string prefix = Platform.Identifier + ".backup-";
        foreach (string backup in Directory.EnumerateDirectories(parent, prefix + "*").OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            if (!Guid.TryParseExact(Path.GetFileName(backup)[prefix.Length..], "N", out _)
                || !IsValidCache(backup, repository)) continue;
            if (!IsValidCache(cacheRoot, repository))
            {
                Directory.Move(backup, cacheRoot + ".previous");
                DirectoryReplacementService.Recover(cacheRoot, path => IsValidCache(path, repository));
            }
            else Directory.Delete(backup, true);
        }
        _loadedCache = null;
    }

    private static bool IsValidCache(string root, string repository)
    {
        if (!Directory.Exists(root)) return false;
        try
        {
            DirectoryReplacementService.RejectLinks(root);
            InstalledBundle? installed = LauncherStore.ReadJson<InstalledBundle>(Path.Combine(root, "installed.json"));
            if (installed == null || installed.Repository != repository || installed.Platform != Platform.Identifier) return false;
            ValidateContent(Path.Combine(root, "content"));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException) { return false; }
    }

    private string DownloadLockPath(string repository) => Path.Combine(store.Home,
        "samples-" + GitHubRepositoryHelper.CacheKey(repository) + "-" + Platform.Identifier + ".lock");

    private async Task<FileStream> AcquireDownloadLockAsync(string repository, CancellationToken token)
    {
        string path = DownloadLockPath(repository);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(100, token);
            }
        }
    }

    private static async Task CopyDirectoryAsync(string source, string output, CancellationToken token)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            string destination = LauncherStore.SafeChildPath(output, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using FileStream input = File.OpenRead(file);
            await using FileStream target = File.Create(destination);
            await input.CopyToAsync(target, token);
        }
    }

    private static async Task ExtractArchiveAsync(ZipArchive archive, string outputDirectory, CancellationToken token)
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if ((entry.ExternalAttributes >> 16 & 0xf000) == 0xa000)
            {
                throw new InvalidDataException("Sample bundles cannot contain symbolic links.");
            }
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
