using System.Security.Cryptography;
using System.Text.RegularExpressions;

using Prowl.Rosetta;

namespace Prowl.Launcher;

/// <summary>The result of a launcher update check, tied to the settings it was made with.</summary>
internal sealed record LauncherUpdateCheck(string Repository, bool Prereleases, EditorRelease? Release)
{
    /// <summary>Whether the check still applies after the user may have changed repository or channel.</summary>
    internal bool Matches(Settings settings) =>
        Prereleases == settings.LauncherPrereleases
        && Repository.Equals(settings.LauncherRepository, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
///     Checks for, installs and cleans up launcher updates. Updates are installed side by side under Updates/;
///     the original entry point forwards to the chosen version at startup.
/// </summary>
public sealed class LauncherUpdaterService(HttpClient http, LauncherStore store)
{
    private const string LauncherName = "Prowl.Launcher";

    public static string UpdatesPath(LauncherStore store) => Path.Combine(store.Home, "Updates");

    /// <summary>Held for the whole download, start and save handoff so cleanup never removes an update mid-switch.</summary>
    internal static FileStream LockUpdates(LauncherStore store) => new(
        Path.Combine(store.Home, "launcher-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static bool IsRunning(string target) => ProcessHelper.IsRunningUnder(LauncherName, target);

    private static void ThrowIfRunning(string target)
    {
        if (IsRunning(target))
        {
            throw new IOException("Close the launcher using this version before repairing its update.");
        }
    }

    /// <summary>Per-file hashes written next to an installed update so it can be verified before use.</summary>
    private sealed record Installation(string Digest, string Executable, Dictionary<string, string> Files);

    #region Checking

    /// <summary>
    ///     Finds the newest eligible update. Automatic checks respect the auto-update setting and skip a
    ///     dismissed release (without falling back to an older one).
    /// </summary>
    internal async Task<LauncherUpdateCheck> CheckAsync(string platform, string currentVersion,
        bool automatic, CancellationToken token = default, TimeSpan? timeout = null)
    {
        string repository = GitHubRepository.Normalize(store.Settings.LauncherRepository);
        bool prereleases = store.Settings.LauncherPrereleases;
        if (automatic && !store.Settings.LauncherAutoUpdate)
        {
            return new LauncherUpdateCheck(repository, prereleases, null);
        }

        IReadOnlyList<EditorRelease> releases =
            await new GitHubReleasesService(http, store, repository).GetAsync(token, timeout);
        EditorRelease? release = FindUpdate(releases, platform, currentVersion, prereleases);
        if (automatic && release != null && IsDismissed(store.Settings, repository, release.Id))
        {
            release = null;
        }

        return new LauncherUpdateCheck(repository, prereleases, release);
    }

    internal static bool IsDismissed(Settings settings, string repository, long releaseId) =>
        settings.DismissedLauncherReleases.TryGetValue(DismissalKey(repository, releaseId), out bool dismissed) &&
        dismissed;

    internal static void Dismiss(LauncherStore store, string repository, long releaseId)
    {
        store.Settings.DismissedLauncherReleases[DismissalKey(repository, releaseId)] = true;
        store.Save();
    }

    private static string DismissalKey(string repository, long releaseId) =>
        $"{GitHubRepository.CacheKey(repository)}-{releaseId}";

    /// <summary>The launcher version in an asset named like <c>Prowl.Launcher-1.2.3-win-x64.zip</c>, or null.</summary>
    public static string? VersionFor(ReleaseAsset asset, string platform)
    {
        Match match = Regex.Match(asset.Name,
            $"\\AProwl[- .]Launcher-({VersionHelper.Pattern})-{Regex.Escape(platform)}\\.zip\\z");
        return match.Success && VersionHelper.TryParse(match.Groups[1].Value, out _) ? match.Groups[1].Value : null;
    }

    public static ReleaseAsset? AssetFor(EditorRelease release, string platform) =>
        release.Assets.FirstOrDefault(asset => VersionFor(asset, platform) != null);

    /// <summary>
    ///     The newest release newer than <paramref name="currentVersion" />. When prereleases are excluded and the
    ///     current version is a prerelease, the newest stable release is returned so users can switch channel.
    /// </summary>
    public static EditorRelease? FindUpdate(IEnumerable<EditorRelease> releases, string platform, string currentVersion,
        bool includePrereleases = false)
    {
        if (!VersionHelper.TryParse(currentVersion, out ReleaseVersion installed))
        {
            return null;
        }

        EditorRelease? newest = null;
        ReleaseVersion? newestVersion = !includePrereleases && installed.IsPrerelease ? null : installed;
        foreach (EditorRelease release in releases.Where(release =>
                     !release.Draft && (includePrereleases || !release.Preview)))
        {
            ReleaseAsset? asset = AssetFor(release, platform);
            string? version = asset == null ? null : VersionFor(asset, platform);
            if (!VersionHelper.TryParse(version, out ReleaseVersion candidate)
                || (!includePrereleases && candidate.IsPrerelease)
                || (newestVersion != null && candidate.CompareTo(newestVersion) <= 0))
            {
                continue;
            }

            newest = release;
            newestVersion = candidate;
        }

        return newest;
    }

    #endregion

    #region Installing

    /// <summary>Installs (or verifies and reuses) the update and returns its executable.</summary>
    public async Task<string> InstallAsync(
        EditorRelease release,
        string platform,
        IProgress<TransferProgress>? progress = null,
        CancellationToken token = default
    )
    {
        bool allowed = !release.Draft
                       && (!release.Preview || store.Settings.LauncherPrereleases)
                       && release.Id > 0
                       && Constants.Storage.SupportedPlatforms.Contains(platform);
        if (!allowed)
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_launcher_release"));
        }

        ReleaseAsset asset = AssetFor(release, platform) ??
                             throw new InvalidDataException(Loc.Get("launcher.errors.no_platform_update"));
        string repository = GitHubRepository.Normalize(store.Settings.LauncherRepository);
        ReleasePackage.RequireFromRepository(asset, repository);
        string version = VersionFor(asset, platform)!;
        if (!store.Settings.LauncherPrereleases && VersionHelper.IsPreview(version))
        {
            throw new InvalidDataException(Loc.Get("launcher.errors.invalid_launcher_release"));
        }

        string versions = Path.Combine(UpdatesPath(store), GitHubRepository.CacheKey(repository));
        string target = FileSystemHelper.SafeChildPath(versions, $"{version}-{platform}");
        string name = Platform.ExecutableName(LauncherName, platform);
        Directory.CreateDirectory(versions);
        FileSystemHelper.RejectLink(store.Home);
        FileSystemHelper.RejectLink(UpdatesPath(store));
        FileSystemHelper.RejectLink(versions);

        using FileStream operation = store.AcquireOperationLock();
        string? executable = await Task.Run(() => ValidateInstallation(target, asset.Digest, name, token), token);
        if (executable == null)
        {
            ThrowIfRunning(target);
        }

        await Task.Run(
            () => DirectoryReplacement.Recover(target, path => ValidateInstallation(path, null, name, token) != null),
            token);
        executable = await Task.Run(() => ValidateInstallation(target, asset.Digest, name, token), token);
        return executable ?? await DownloadAndReplaceAsync(asset, target, name, progress, token);
    }

    private async Task<string> DownloadAndReplaceAsync(ReleaseAsset asset, string target, string name,
        IProgress<TransferProgress>? progress, CancellationToken token)
    {
        string work = Path.Combine(store.WorkPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        FileSystemHelper.RejectLink(store.WorkPath);
        try
        {
            string archive = Path.Combine(work, "launcher.zip");
            await ReleasePackage.DownloadVerifiedAsync(http, asset, archive, progress, token, store.DownloadsPath);
            string extracted = Path.Combine(work, "extracted");
            await Task.Run(() => ReleasePackage.ExtractArchive(archive, extracted, token), token);
            string entry = Directory.EnumerateFiles(extracted, name, SearchOption.AllDirectories).FirstOrDefault()
                           ?? throw new InvalidDataException(Loc.Get("launcher.errors.missing_launcher"));
            ReleasePackage.MarkExecutable(entry);

            await Task.Run(() => WriteInstallation(extracted, entry, asset.Digest!, token), token);
            token.ThrowIfCancellationRequested();
            ThrowIfRunning(target);
            DirectoryReplacement.Replace(extracted, target);
            return FileSystemHelper.SafeChildPath(target, Path.GetRelativePath(extracted, entry));
        }
        finally
        {
            // A locked leftover file must not hide the install result or its real error.
            try
            {
                Directory.Delete(work, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void WriteInstallation(string root, string executable, string digest, CancellationToken token)
    {
        Dictionary<string, string> files = [];
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(root, path);
            if (relative != Constants.Storage.LauncherManifest)
            {
                files.Add(relative, HashFile(path));
            }
        }

        JsonFile.Write(Path.Combine(root, Constants.Storage.LauncherManifest),
            new Installation(digest, Path.GetRelativePath(root, executable), files));
    }

    /// <summary>The executable of a complete, unmodified installation at <paramref name="root" />, or null.</summary>
    private static string? ValidateInstallation(string root, string? digest, string name, CancellationToken token)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            FileSystemHelper.RejectLinks(root);
            Installation? installed =
                JsonFile.Read<Installation>(Path.Combine(root, Constants.Storage.LauncherManifest));
            bool consistent = installed?.Files is { Count: > 0 }
                              && (digest == null || string.Equals(digest, installed.Digest,
                                  StringComparison.OrdinalIgnoreCase))
                              && installed.Files.ContainsKey(installed.Executable)
                              && Path.GetFileName(installed.Executable) == name;
            if (!consistent)
            {
                return null;
            }

            foreach ((string relative, string hash) in installed!.Files)
            {
                token.ThrowIfCancellationRequested();
                if (!HashFile(FileSystemHelper.SafeChildPath(root, relative))
                        .Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return FileSystemHelper.SafeChildPath(root, installed.Executable);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException
                                          or ArgumentException)
        {
            return null;
        }
    }

    private static string HashFile(string path)
    {
        using FileStream input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    #endregion

    #region Cleanup

    /// <summary>
    ///     Deletes update folders that are not the active, installed or running launcher.
    ///     Also removes unused copies from the former LauncherVersions storage directory.
    /// </summary>
    internal static void Cleanup(LauncherStore store)
    {
        using FileStream update = LockUpdates(store);
        using FileStream operation = store.AcquireOperationLock();
        // Another launcher may have activated a different update since this one loaded its settings.
        Settings saved = JsonFile.Read<Settings>(store.SettingsPath) ?? store.Settings;
        FileSystemHelper.RejectLink(store.Home);
        foreach (string root in new[] { UpdatesPath(store), Path.Combine(store.Home, "LauncherVersions") })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            FileSystemHelper.RejectLink(root);
            foreach (string repository in Directory.EnumerateDirectories(root))
            {
                FileSystemHelper.RejectLink(repository);
                foreach (string version in Directory.EnumerateDirectories(repository))
                {
                    string target = FileSystemHelper.SafeChildPath(repository, Path.GetFileName(version));
                    if (!IsInUse(target, saved))
                    {
                        FileSystemHelper.RejectLinks(target);
                        Directory.Delete(target, true);
                    }
                }

                DeleteIfEmpty(repository);
            }

            DeleteIfEmpty(root);
        }
    }

    private static bool IsInUse(string target, Settings saved) =>
        FileSystemHelper.IsInside(target, saved.LauncherExecutable)
        || FileSystemHelper.IsInside(target, saved.InstalledLauncherExecutable)
        || FileSystemHelper.IsInside(target, Environment.ProcessPath)
        || IsRunning(target);

    private static void DeleteIfEmpty(string directory)
    {
        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    #endregion
}
