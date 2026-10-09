using System.Net;
using System.Text.Json;
using Xunit;

namespace Prowl.Launcher.Test;

public sealed class LauncherUpdateCheckTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlUpdateCheckTests", Guid.NewGuid().ToString("N"));
    private static EditorRelease Release(long id, string version, bool preview = false) => new(id, "v" + version,
        preview, false, DateTimeOffset.UtcNow, "https://github.com/dimmerly/Prowl-Launcher/releases/tag/v" + version,
        "## Changes\n\n- A new feature\n- A bug fix", [new ReleaseAsset($"Prowl-Launcher-{version}-win-x64.zip", "", 1, null)]);

    private static HttpClient Client(params EditorRelease[] releases) => new(new Handler(_ =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(releases)) })));

    [Fact]
    public async Task OptingIntoPrereleasesSelectsPreviewAndKeepsItsChangelog()
    {
        LauncherStore store = new(_home);
        using HttpClient http = Client(Release(1, "1.1.0"), Release(2, "1.2.0-preview.1", true));
        LauncherUpdateCheckService service = new(http, store);
        Assert.Equal(1, (await service.CheckAsync("win-x64", "1.0.0", true)).Release!.Id);
        store.Settings.LauncherPrereleases = true;
        LauncherUpdateCheck check = await service.CheckAsync("win-x64", "1.0.0", true);
        Assert.Equal(2, check.Release!.Id);
        Assert.Contains("A new feature", check.Release.Notes);
        Assert.True(check.Matches(store.Settings));
    }

    [Fact]
    public async Task StartupDismissalSurvivesRestartAndManualCheckCanStillOfferIt()
    {
        LauncherStore store = new(_home);
        LauncherUpdateCheckService.Dismiss(store, store.Settings.LauncherRepository, 2);
        store = new LauncherStore(_home);
        using HttpClient http = Client(Release(1, "1.1.0"), Release(2, "1.2.0"));
        LauncherUpdateCheckService service = new(http, store);
        Assert.Null((await service.CheckAsync("win-x64", "1.0.0", true)).Release);
        Assert.Equal(2, (await service.CheckAsync("win-x64", "1.0.0", false)).Release!.Id);
    }

    [Fact]
    public async Task ANewReleaseIsOfferedEvenAfterDismissingThePreviousOne()
    {
        LauncherStore store = new(_home);
        LauncherUpdateCheckService.Dismiss(store, store.Settings.LauncherRepository, 1);
        using HttpClient http = Client(Release(1, "1.1.0"), Release(2, "1.2.0"));
        Assert.Equal(2, (await new LauncherUpdateCheckService(http, store).CheckAsync("win-x64", "1.0.0", true)).Release!.Id);
    }

    [Fact]
    public async Task DismissalsStayScopedToRepositoryAndPersistAcrossChannelChanges()
    {
        LauncherStore store = new(_home);
        string repository = store.Settings.LauncherRepository;
        LauncherUpdateCheckService.Dismiss(store, repository, 1);
        LauncherUpdateCheckService.Dismiss(store, repository, 2);
        store.Settings.LauncherPrereleases = true;
        store.Save();
        using HttpClient http = Client(Release(1, "1.1.0"), Release(2, "1.2.0-preview.1", true));
        LauncherUpdateCheckService service = new(http, store);
        Assert.Null((await service.CheckAsync("win-x64", "1.0.0", true)).Release);
        store.Settings.LauncherPrereleases = false;
        Assert.Null((await service.CheckAsync("win-x64", "1.0.0", true)).Release);
        store.Settings.LauncherRepository = "someone/custom-launcher";
        Assert.Equal(1, (await service.CheckAsync("win-x64", "1.0.0", true)).Release!.Id);
    }

    [Fact]
    public void ConcurrentDismissalsDoNotOverwriteEachOther()
    {
        LauncherStore first = new(_home), second = new(_home);
        LauncherUpdateCheckService.Dismiss(first, first.Settings.LauncherRepository, 1);
        LauncherUpdateCheckService.Dismiss(second, second.Settings.LauncherRepository, 2);
        Settings saved = new LauncherStore(_home).Settings;
        Assert.True(LauncherUpdateCheckService.IsDismissed(saved, saved.LauncherRepository, 1));
        Assert.True(LauncherUpdateCheckService.IsDismissed(saved, saved.LauncherRepository, 2));
    }

    [Fact]
    public async Task CheckRetainsItsSourceAndChannelSoStaleResultsCanBeDiscarded()
    {
        LauncherStore store = new(_home);
        TaskCompletionSource<HttpResponseMessage> response = new();
        using HttpClient http = new(new Handler(request =>
        {
            Assert.Equal("/repos/dimmerly/Prowl-Launcher/releases", request.RequestUri!.AbsolutePath);
            return response.Task;
        }));
        Task<LauncherUpdateCheck> pending = new LauncherUpdateCheckService(http, store).CheckAsync("win-x64", "1.0.0", true);
        store.Settings.LauncherPrereleases = true;
        store.Settings.LauncherRepository = "someone/custom-launcher";
        response.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new[] { Release(1, "1.1.0") })) });
        LauncherUpdateCheck check = await pending;
        Assert.Equal("dimmerly/Prowl-Launcher", check.Repository);
        Assert.False(check.Prereleases);
        Assert.False(check.Matches(store.Settings));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request);
    }

    public void Dispose() { if (Directory.Exists(_home)) Directory.Delete(_home, true); }
}
