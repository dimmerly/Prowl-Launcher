namespace Prowl.Launcher;

internal sealed record LauncherUpdateCheck(string Repository, bool Prereleases, EditorRelease? Release, bool UsedCache)
{
    internal bool Matches(Settings settings) => Prereleases == settings.LauncherPrereleases
        && Repository.Equals(settings.LauncherRepository, StringComparison.OrdinalIgnoreCase);
}

internal sealed class LauncherUpdateCheckService(HttpClient http, LauncherStore store)
{
    internal async Task<LauncherUpdateCheck> CheckAsync(string platform, string currentVersion,
        bool automatic, CancellationToken token = default)
    {
        string repository = GitHubRepositoryHelper.Normalize(store.Settings.LauncherRepository);
        bool prereleases = store.Settings.LauncherPrereleases;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        GitHubReleasesService github = new(http, store, repository);
        IReadOnlyList<EditorRelease> releases = await github.GetAsync(timeout.Token);
        EditorRelease? release = LauncherUpdaterService.FindUpdate(releases, platform, currentVersion, prereleases);
        // Select the newest eligible release first; dismissal must not offer an older release instead.
        if (automatic && release != null && IsDismissed(store.Settings, repository, release.Id)) release = null;
        return new(repository, prereleases, release, github.UsedCache);
    }

    internal static bool IsDismissed(Settings settings, string repository, long releaseId) =>
        settings.DismissedLauncherReleases.TryGetValue(DismissalKey(repository, releaseId), out bool dismissed) && dismissed;

    internal static void Dismiss(LauncherStore store, string repository, long releaseId)
    {
        store.Settings.DismissedLauncherReleases[DismissalKey(repository, releaseId)] = true;
        store.Save();
    }

    private static string DismissalKey(string repository, long releaseId) => $"{GitHubRepositoryHelper.CacheKey(repository)}-{releaseId}";
}
