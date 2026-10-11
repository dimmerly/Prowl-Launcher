using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prowl.Rosetta;
namespace Prowl.Launcher;

public sealed class GitHubReleasesService(HttpClient http, LauncherStore store, string? sourceRepository = null)
{
    public bool UsedCache
    {
        get;
        private set;
    }
    public static string CachePath(LauncherStore store) => Path.Combine(store.Home, "releases.json");

    internal static List<EditorRelease>? ReadCache(LauncherStore store, string repository)
    {
        EditorCache? cache = JsonFile.Read<EditorCache>(CachePath(store));
        return cache != null && string.Equals(cache.Repository, GitHubRepository.Normalize(repository), StringComparison.OrdinalIgnoreCase)
            ? cache.Releases : null;
    }

    public async Task<IReadOnlyList<EditorRelease>> GetAsync(CancellationToken token = default, TimeSpan? timeout = null)
    {
        UsedCache = false;
        string repository = GitHubRepository.Normalize(sourceRepository ?? store.Settings.ProwlRepository);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? Constants.Network.ReleaseCheckTimeout);
        try
        {
            List<EditorRelease> releases = [];
            // Pagination includes older versions and prereleases; /latest would hide previews.
            for (int page = 1;; page++)
            {
                using HttpRequestMessage request = new( HttpMethod.Get, $"{Constants.Network.GitHubApiUrl}repos/{repository}/releases?per_page={Constants.Network.ReleasesPerPage}&page={page}" );
                request.Headers.UserAgent.ParseAdd(Constants.Network.UserAgent);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using HttpResponseMessage response = await http.SendAsync(request, deadline.Token);
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                {
                    throw new HttpRequestException(Loc.Get("launcher.errors.github_rate_limit"));
                }

                response.EnsureSuccessStatusCode();
                List<EditorRelease> batch = await response.Content.ReadFromJsonAsync<List<EditorRelease>>(deadline.Token) ?? [];
                releases.AddRange(batch.Where(r => !r.Draft));
                if (batch.Count < Constants.Network.ReleasesPerPage)
                {
                    break;
                }
            }

            EditorRelease[] sorted = releases.OrderByDescending(r => r.Published).ToArray();
            if (sourceRepository == null)
            {
                JsonFile.Write(CachePath(store), new EditorCache(repository, sorted.ToList()));
            }
            return sorted;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !token.IsCancellationRequested)
        {
            List<EditorRelease>? cached = sourceRepository == null ? ReadCache(store, repository) : null;
            if (cached == null)
            {
                if (e is OperationCanceledException)
                {
                    throw new HttpRequestException("The GitHub release request timed out.", e);
                }
                throw;
            }

            UsedCache = true;
            return cached;
        }
    }
    internal sealed record EditorCache(string Repository, List<EditorRelease> Releases);
}
