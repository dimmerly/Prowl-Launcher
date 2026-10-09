using Prowl.Rosetta;
using System.Text.RegularExpressions;

namespace Prowl.Launcher;

/// <summary>Install launcher updates side by side; the original entry point forwards to the chosen version.</summary>
public sealed class LauncherUpdaterService(HttpClient http, LauncherStore store)
{
    private const string VersionPattern = @"\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?";

    public static bool IsNewer(string version, string currentVersion)
    {
        if (!Regex.IsMatch(version, $"^{VersionPattern}$") || !Regex.IsMatch(currentVersion, $"^{VersionPattern}$"))
        {
            return false;
        }

        string[] candidate = version.Split('+')[0].Split('-', 2);
        string[] current = currentVersion.Split('+')[0].Split('-', 2);
        if (!Version.TryParse(candidate[0], out Version? next) || !Version.TryParse(current[0], out Version? installed))
        {
            return false;
        }

        int comparison = next.CompareTo(installed);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        if (candidate.Length == 1 || current.Length == 1)
        {
            return candidate.Length < current.Length;
        }

        string[] nextPreview = candidate[1].Split('.'), currentPreview = current[1].Split('.');
        for (int i = 0; i < Math.Min(nextPreview.Length, currentPreview.Length); i++)
        {
            bool nextNumeric = long.TryParse(nextPreview[i], out long nextNumber);
            bool currentNumeric = long.TryParse(currentPreview[i], out long currentNumber);
            comparison = nextNumeric && currentNumeric ? nextNumber.CompareTo(currentNumber) : nextNumeric != currentNumeric ? nextNumeric ? -1 : 1 : string.CompareOrdinal(nextPreview[i], currentPreview[i]);
            if (comparison != 0)
            {
                return comparison > 0;
            }
        }

        return nextPreview.Length > currentPreview.Length;
    }

    public static string? VersionFor(ReleaseAsset asset, string platform)
    {
        Match match = Regex.Match(asset.Name, $"^Prowl[- .]Launcher-({VersionPattern})-{Regex.Escape(platform)}\\.zip$");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static ReleaseAsset? AssetFor(EditorRelease release, string platform) => release.Assets
        .FirstOrDefault(asset => VersionFor(asset, platform) != null);

    public static EditorRelease? FindUpdate(IEnumerable<EditorRelease> releases, string platform, string currentVersion, bool includePrereleases = false)
    {
        EditorRelease? newest = null;
        string? newestVersion = !includePrereleases && currentVersion.Split('+')[0].Contains('-') ? null : currentVersion;
        foreach (EditorRelease release in releases.Where(release => !release.Draft && (includePrereleases || !release.Preview)))
        {
            ReleaseAsset? asset = AssetFor(release, platform);
            string? version = asset == null ? null : VersionFor(asset, platform);
            if (version == null || (!includePrereleases && version.Split('+')[0].Contains('-'))
                || (newestVersion != null && !IsNewer(version, newestVersion)))
                continue;

            newest = release;
            newestVersion = version;
        }
        return newest;
    }

    public async Task<string> InstallAsync(
        EditorRelease release,
        string platform,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default
    )
    {
        if (release.Draft || (release.Preview && !store.Settings.LauncherPrereleases)
            || release.Id <= 0 || !LauncherStore.SupportedPlatforms.Contains(platform))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_launcher_release"));
        }

        ReleaseAsset asset = AssetFor(release, platform) ?? throw new InvalidDataException(Loc.Get("launcher.errors.no_platform_update"));
        string repository = GitHubRepositoryHelper.Normalize(store.Settings.LauncherRepository);
        if (!Uri.TryCreate(asset.DownloadUrl, UriKind.Absolute, out Uri? source)
            || source.Scheme != "https" || source.Host != "github.com"
            || !source.AbsolutePath.StartsWith($"/{repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_download_source"));

        string version = VersionFor(asset, platform)!;
        if (!store.Settings.LauncherPrereleases && version.Split('+')[0].Contains('-'))
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_launcher_release"));

        string versions = Path.Combine(store.Home, "LauncherVersions", GitHubRepositoryHelper.CacheKey(repository));
        string target = LauncherStore.SafeChildPath(versions, $"{version}-{platform}");
        string name = platform.StartsWith("win-", StringComparison.Ordinal) ? "Prowl.Launcher.exe" : "Prowl.Launcher";
        Directory.CreateDirectory(versions);
        using FileStream operation = new(
            Path.Combine(store.Home, "operations.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        string? executable = Directory.Exists(target) ? Directory.EnumerateFiles(target, name, SearchOption.AllDirectories).FirstOrDefault() : null;
        if (executable == null)
        {
            string work = Path.Combine(store.Home, "Work", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string archive = Path.Combine(work, "launcher.zip");
                await PackageDownloadService.DownloadVerifiedAsync(http, asset, archive, progress, token);
                string extracted = Path.Combine(work, "extracted");
                await Task.Run(() => EditorInstallerService.ExtractArchive(archive, extracted, token), token);
                string? entry = Directory.EnumerateFiles(extracted, name, SearchOption.AllDirectories).FirstOrDefault();
                if (entry == null)
                {
                    throw new InvalidDataException(Loc.Get("launcher.errors.missing_launcher"));
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(entry, File.GetUnixFileMode(entry) | UnixFileMode.UserExecute);
                }

                token.ThrowIfCancellationRequested();
                if (Directory.Exists(target))
                {
                    throw new IOException(Loc.Get("launcher.errors.incomplete_update"));
                }

                Directory.Move(extracted, target);
                executable = LauncherStore.SafeChildPath(target, Path.GetRelativePath(extracted, entry));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        store.Settings.LauncherExecutable = executable;
        store.Save();
        return executable;
    }
}
