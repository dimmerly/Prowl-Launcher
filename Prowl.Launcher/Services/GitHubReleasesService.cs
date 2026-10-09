using Prowl.Rosetta;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Prowl.Launcher;

public sealed class GitHubReleasesService(HttpClient http, LauncherStore store, string? sourceRepository = null)
{
    public static string CachePath(LauncherStore store, string repository) => Path.Combine(
        store.Home,
        repository.Equals(GitHubRepositoryHelper.DefaultProwl, StringComparison.OrdinalIgnoreCase) ? "releases.json" : "releases-" + GitHubRepositoryHelper.CacheKey(repository) + ".json"
    );
    public bool UsedCache
    {
        get;
        private set;
    }

    public async Task<IReadOnlyList<EditorRelease>> GetAsync(CancellationToken token = default)
    {
        UsedCache = false;
        string repository = GitHubRepositoryHelper.Normalize(sourceRepository ?? store.Settings.ProwlRepository);
        string cachePath = CachePath(store, repository);
        try
        {
            List<EditorRelease> releases = [];
            // Pagination includes older versions and prereleases; /latest would hide previews.
            for (int page = 1;; page++)
            {
                using HttpRequestMessage request = new( HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}" );
                request.Headers.UserAgent.ParseAdd("Prowl-Launcher/1.0");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using HttpResponseMessage response = await http.SendAsync(request, token);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException(Loc.Get("launcher.errors.github_rate_limit"));
                }

                response.EnsureSuccessStatusCode();
                List<EditorRelease> batch = await response.Content.ReadFromJsonAsync<List<EditorRelease>>(token) ?? [];
                releases.AddRange(batch.Where(r => !r.Draft));
                if (batch.Count < 100)
                {
                    break;
                }
            }

            EditorRelease[] sorted = releases.OrderByDescending(r => r.Published).ToArray();
            LauncherStore.WriteJson(cachePath, sorted);
            return sorted;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !token.IsCancellationRequested)
        {
            List<EditorRelease>? cached = LauncherStore.ReadJson<List<EditorRelease>>(cachePath);
            if (cached == null)
            {
                throw;
            }

            UsedCache = true;
            return cached;
        }
    }
}
