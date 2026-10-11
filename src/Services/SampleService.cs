using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>
///     Downloads the sample bundle published with launcher releases and runs samples from it. The bundle is
///     cached under Samples/ and replaced atomically; each run gets its own copy under Work/.
/// </summary>
sealed class SampleService
{
    private const string SampleHostName = "Prowl.SampleHost";
    private const string InstalledFile = "installed.json";

    /// <summary>The samples built into the launcher, shown before a bundle has been downloaded.</summary>
    public static IReadOnlyList<Sample> Samples
    {
        get;
    } = LoadEmbeddedCatalog();

    public bool IsCached => InstalledCatalog() != null;
    public IReadOnlyList<Sample> Catalog => InstalledCatalog() ?? Samples;

    private string Repository => GitHubRepository.Normalize(_store.Settings.LauncherRepository);
    private string CacheRoot => Path.Combine(_store.Home, "Samples");
    private string ContentPath => Path.Combine(CacheRoot, "content");
    private string DownloadLockPath => Path.Combine(_store.Home, "samples.lock");

    private readonly HttpClient _http;
    private readonly LauncherStore _store;
    private IReadOnlyList<Sample>? _loadedCatalog;
    private string? _loadedRepository;
    private DateTime _loadedStamp;

    public SampleService(HttpClient http, LauncherStore store)
    {
        _http = http;
        _store = store;
        try
        {
            RecoverInterruptedOperations();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            LauncherLog.Append(store.Home, $"Sample recovery: {error}");
        }
    }

    public bool IsAvailable(Sample sample) => Catalog.Any(known => known.Id == sample.Id);

    internal static IReadOnlyList<Sample> Discover(IEnumerable<string> ids) => ids
        .Where(id => Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_-]*\z"))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .Select(id => new Sample(id))
        .ToArray();

    private static IReadOnlyList<Sample> LoadEmbeddedCatalog()
    {
        using Stream stream = typeof( Launcher ).Assembly.GetManifestResourceStream("Prowl.Launcher.Samples.catalog.json")!;
        return Discover(JsonSerializer.Deserialize<string[]>(stream) ?? []);
    }

    private static async Task CopyDirectoryAsync(string source, string output, CancellationToken token)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            string destination = FileSystemHelper.SafeChildPath(output, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using FileStream input = File.OpenRead(file);
            await using FileStream target = File.Create(destination);
            await input.CopyToAsync(target, token);
        }
    }

    /// <summary>Which bundle is cached, so updates and repository changes can be detected.</summary>
    private sealed record InstalledBundle(string Repository, string Platform, long ReleaseId, string Tag, string Digest);

    #region Running

    public async Task RunAsync(Sample sample, IProgress<TransferProgress>? progress, CancellationToken token, bool allowNetwork = true)
    {
        token.ThrowIfCancellationRequested();
        // Each running sample owns its output, including any files it keeps open.
        string output = Path.Combine(_store.WorkPath, "sample-" + Guid.NewGuid().ToString("N"));
        string assembly = await ExtractAsync(sample, output, token, progress, allowNetwork);
        token.ThrowIfCancellationRequested();
        Process process = Process.Start(LaunchInfo(assembly))
                          ?? throw new InvalidOperationException(Loc.Get("launcher.samples.start_failed"));
        Task<int> completion = MonitorAsync(process, output);
        // Report startup crashes through the operation panel, retaining logs for later failures.
        Task startup = Task.Delay(Constants.Startup.SampleStartupMilliseconds, CancellationToken.None);
        if (await Task.WhenAny(completion, startup) == completion && await completion != 0)
        {
            throw new InvalidOperationException(Loc.Get("launcher.samples.start_failed"));
        }
    }

    /// <summary>Runs the sample host next to the extracted sample assembly.</summary>
    internal static ProcessStartInfo LaunchInfo(string assemblyPath)
    {
        string directory = Path.GetDirectoryName(assemblyPath)!;
        string host = Path.Combine(directory, Platform.ExecutableName(SampleHostName));
        return ProcessHelper.Captured(host, directory, "--run-sample", assemblyPath);
    }

    /// <summary>Saves the sample's output to logs; the output folder is removed only after a clean exit.</summary>
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

    /// <summary>Copies the sample host and one sample into <paramref name="outputDirectory" />, returning the sample assembly.</summary>
    public async Task<string> ExtractAsync(Sample sample, string outputDirectory, CancellationToken token = default,
        IProgress<TransferProgress>? progress = null, bool allowNetwork = true)
    {
        token.ThrowIfCancellationRequested();
        RequireAvailable(sample);
        await EnsureDownloadedAsync(progress, token, allowNetwork);
        await using FileStream copyLock = await AcquireDownloadLockAsync(token);
        RecoverCache();
        RequireAvailable(sample);
        try
        {
            await CopyDirectoryAsync(Path.Combine(ContentPath, "Host"), outputDirectory, token);
            await CopyDirectoryAsync(Path.Combine(ContentPath, "Samples", sample.Id), outputDirectory, token);
            ReleasePackage.MarkExecutable(Path.Combine(outputDirectory, SampleHostName));
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

    private void RequireAvailable(Sample sample)
    {
        if (!IsAvailable(sample))
        {
            throw new InvalidOperationException(Loc.Get("launcher.samples.unavailable"));
        }
    }

    #endregion

    #region Bundle

    /// <summary>
    ///     Downloads the newest bundle unless one is cached. With <paramref name="checkForUpdates" />, a cached
    ///     bundle is replaced when the release offers a different one.
    /// </summary>
    public async Task EnsureDownloadedAsync(IProgress<TransferProgress>? progress = null, CancellationToken token = default,
        bool allowNetwork = true, bool checkForUpdates = false)
    {
        token.ThrowIfCancellationRequested();
        string repository = Repository;
        Directory.CreateDirectory(_store.Home);
        await using FileStream downloadLock = await AcquireDownloadLockAsync(token);
        RecoverCache();
        if (IsCached && !checkForUpdates)
        {
            return;
        }
        if (!allowNetwork)
        {
            throw new InvalidOperationException(Loc.Get("launcher.samples.download_required"));
        }

        progress?.Report(new TransferProgress("launcher.samples.downloading"));
        (EditorRelease release, ReleaseAsset asset) = await FindBundleAsync(repository, token)
                                                      ?? throw new InvalidOperationException(Loc.Get("launcher.samples.no_bundle"));
        InstalledBundle? installed = JsonFile.Read<InstalledBundle>(Path.Combine(CacheRoot, InstalledFile));
        if (IsCached && string.Equals(installed?.Digest, asset.Digest, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        ReleasePackage.RequireFromRepository(asset, repository, release.Tag);

        await DownloadBundleAsync(repository, release, asset, progress, token);
    }

    private async Task DownloadBundleAsync(string repository, EditorRelease release, ReleaseAsset asset,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(_store.WorkPath);
        string staging = Path.Combine(_store.WorkPath, "samples-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            string archive = Path.Combine(staging, "samples.zip");
            await ReleasePackage.DownloadVerifiedAsync(_http, asset, archive, progress, token);
            progress?.Report(new TransferProgress("launcher.samples.extracting"));
            string content = Path.Combine(staging, "content");
            await Task.Run(() => ReleasePackage.ExtractArchive(archive, content, token), token);
            ValidateContent(content);
            JsonFile.Write(Path.Combine(staging, InstalledFile),
                new InstalledBundle(repository, Platform.Identifier, release.Id, release.Tag, asset.Digest!));
            File.Delete(archive);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(CacheRoot)!);
            DirectoryReplacement.Replace(staging, CacheRoot);
            _loadedRepository = null;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, true);
            }
        }
    }

    public async Task<bool> HasUpdateAsync(CancellationToken token = default)
    {
        if (!IsCached)
        {
            return false;
        }
        InstalledBundle installed = JsonFile.Read<InstalledBundle>(Path.Combine(CacheRoot, InstalledFile))!;
        return await FindBundleAsync(Repository, token) is var (release, asset)
               && ReleasePackage.HasValidDigest(asset)
               && release.Id != installed.ReleaseId
               && !string.Equals(asset.Digest, installed.Digest, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(EditorRelease Release, ReleaseAsset Asset)?> FindBundleAsync(string repository, CancellationToken token)
    {
        IReadOnlyList<EditorRelease> releases = await new GitHubReleasesService(_http, _store, repository).GetAsync(token);
        return SelectBundle(releases, Platform.Identifier, _store.Settings.LauncherPrereleases);
    }

    /// <summary>The newest non-draft release with a bundle for <paramref name="platform" />.</summary>
    internal static (EditorRelease Release, ReleaseAsset Asset)? SelectBundle(IEnumerable<EditorRelease> releases, string platform, bool previews)
    {
        IEnumerable<EditorRelease> candidates = releases
            .Where(r => !r.Draft && r.Id > 0 && (previews || !r.Preview))
            .OrderByDescending(r => r.Published);
        foreach (EditorRelease release in candidates)
        {
            if (release.Assets.FirstOrDefault(a => a.Name == $"Prowl-Samples-{platform}.zip") is {} asset)
            {
                return (release, asset);
            }
        }
        return null;
    }

    /// <summary>The cached bundle's samples, or null when nothing valid is cached for this repository and platform.</summary>
    private IReadOnlyList<Sample>? InstalledCatalog()
    {
        string metadata = Path.Combine(CacheRoot, InstalledFile);
        if (_loadedRepository == Repository && File.GetLastWriteTimeUtc(metadata) == _loadedStamp)
        {
            return _loadedCatalog;
        }
        InstalledBundle? installed = JsonFile.Read<InstalledBundle>(metadata);
        if (installed == null || installed.Repository != Repository || installed.Platform != Platform.Identifier)
        {
            return null;
        }
        try
        {
            _loadedCatalog = ValidateContent(ContentPath);
            _loadedRepository = Repository;
            _loadedStamp = File.GetLastWriteTimeUtc(metadata);
            return _loadedCatalog;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Checks the bundle has a catalog, the sample host and every listed sample.</summary>
    internal static IReadOnlyList<Sample> ValidateContent(string root)
    {
        string[] ids = JsonFile.Read<string[]>(Path.Combine(root, "samples.json"))
                       ?? throw new InvalidDataException("The sample bundle has no catalog.");
        IReadOnlyList<Sample> samples = Discover(ids);
        if (samples.Count == 0 || samples.Count != ids.Length)
        {
            throw new InvalidDataException("The sample bundle has an invalid catalog.");
        }

        string host = Path.Combine(root, "Host");
        string[] hostFiles = [Platform.ExecutableName(SampleHostName), SampleHostName + ".dll", SampleHostName + ".runtimeconfig.json"];
        if (!hostFiles.All(file => File.Exists(Path.Combine(host, file))))
        {
            throw new InvalidDataException("The sample bundle is missing its runtime.");
        }
        if (!samples.All(sample => File.Exists(Path.Combine(root, "Samples", sample.Id, sample.Id + ".dll"))))
        {
            throw new InvalidDataException("The sample bundle is missing a sample.");
        }
        return samples;
    }

    #endregion

    #region Locking and recovery

    internal void RecoverInterruptedOperations()
    {
        if (!Directory.Exists(Path.GetDirectoryName(CacheRoot)))
        {
            return;
        }
        FileStream operation;
        // Startup must not wait for another launcher that is downloading a bundle.
        try
        {
            operation = OpenDownloadLock();
        }
        catch (IOException)
        {
            return;
        }
        using (operation)
        {
            RecoverCache();
        }
    }

    private void RecoverCache()
    {
        if (!Directory.Exists(Path.GetDirectoryName(CacheRoot)!))
        {
            return;
        }
        FileSystemHelper.RejectLink(_store.Home);
        if (Directory.Exists(CacheRoot))
        {
            FileSystemHelper.RejectLink(CacheRoot);
        }
        DirectoryReplacement.Recover(CacheRoot, IsValidCache);
        _loadedRepository = null;
    }

    private static bool IsValidCache(string root)
    {
        if (!Directory.Exists(root))
        {
            return false;
        }
        try
        {
            FileSystemHelper.RejectLinks(root);
            InstalledBundle? installed = JsonFile.Read<InstalledBundle>(Path.Combine(root, InstalledFile));
            if (installed == null || installed.Platform != Platform.Identifier)
            {
                return false;
            }
            ValidateContent(Path.Combine(root, "content"));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return false;
        }
    }

    private FileStream OpenDownloadLock() =>
        new( DownloadLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None );

    private async Task<FileStream> AcquireDownloadLockAsync(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return OpenDownloadLock();
            }
            catch (IOException)
            {
                await Task.Delay(Constants.Storage.SampleLockRetryMilliseconds, token);
            }
        }
    }

    #endregion
}
