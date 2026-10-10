using System.Net;

using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Unit")]
public sealed class NewsServiceTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlNewsTests", Guid.NewGuid().ToString("N"));
    private const string Index = """
        [
          {"file":"older.md","title":"Older showcase","date":"2024-01-01"},
          {"file":"newer.md","title":"New devlog","date":"2026-10-10","summary":"Progress","author":"Paper"}
        ]
        """;

    [Fact]
    public void IndexIsSortedNewestFirstAndOptionalMetadataWorks()
    {
        IReadOnlyList<NewsPost> posts = NewsService.ParseIndex(Index);
        Assert.Equal("newer.md", posts[0].File);
        Assert.Equal("Paper", posts[0].Author);
        Assert.Equal("", posts[1].Summary);
        Assert.Equal("", posts[1].Thumbnail);
        Assert.Empty(NewsService.ParseIndex("[]"));
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("https://example.com/post.md")]
    [InlineData("nested/../post.md")]
    [InlineData("/nested/post.md")]
    [InlineData("nested\\post.md")]
    [InlineData("nested//post.md")]
    [InlineData("nested/%2e%2e/post.md")]
    [InlineData("post.md?query")]
    [InlineData("post.txt")]
    public void IndexRejectsPathsOutsideNews(string file)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new[] { new NewsPost(file, "Title", new DateOnly(2026, 10, 10)) });
        Assert.Throws<InvalidDataException>(() => NewsService.ParseIndex(json));
    }

    [Fact]
    public async Task GroupedPostsResolveArticlesImagesAndOfflineCache()
    {
        NewsPost post = new("preview-4/README.md", "Preview 4", new DateOnly(2026, 8, 30),
            Thumbnail: "images/thumbnail.png");
        string index = System.Text.Json.JsonSerializer.Serialize(new[] { post });
        Assert.Equal(post, Assert.Single(NewsService.ParseIndex(index)));
        string local = Path.Combine(_home, "local");
        Directory.CreateDirectory(Path.Combine(local, "preview-4", "images"));
        await File.WriteAllTextAsync(Path.Combine(local, post.File), "# Preview 4");
        await File.WriteAllBytesAsync(Path.Combine(local, "preview-4", "images", "thumbnail.png"), [1, 2, 3]);
        using HttpClient http = new(new Handler(request =>
        {
            Assert.Equal("/team/launcher/main/news/preview-4/README.md", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("# Preview 4") };
        }));
        LauncherStore store = new(_home);
        NewsService localService = new(http, store, "team/launcher", local);
        Assert.Equal("# Preview 4", await localService.GetPostAsync(post, false, default));
        Assert.Equal(new byte[] { 1, 2, 3 },
            await localService.GetImageAsync(localService.ImageSource(post, post.Thumbnail), false, default));
        NewsService remoteService = new(http, store, "team/launcher");
        Assert.Equal("https://raw.githubusercontent.com/team/launcher/main/news/preview-4/images/thumbnail.png",
            remoteService.ImageSource(post, post.Thumbnail).AbsoluteUri);
        Assert.Equal("# Preview 4", await remoteService.GetPostAsync(post, true, default));
        Assert.Equal("# Preview 4", await new NewsService(http, store, "team/launcher").GetPostAsync(post, false, default));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"file\":\"post.md\",\"title\":\"Title\"}]")]
    [InlineData("[{\"file\":\"post.md\",\"title\":\" \",\"date\":\"2026-10-10\"}]")]
    public void IndexRejectsMissingRequiredFields(string json) =>
        Assert.Throws<InvalidDataException>(() => NewsService.ParseIndex(json));

    [Fact]
    public void IndexRejectsDuplicateFiles() =>
        Assert.Throws<InvalidDataException>(() => NewsService.ParseIndex(Index.Replace("older.md", "newer.md")));

    [Fact]
    public async Task IndexAndArticlesAreCachedAndScopedToRepository()
    {
        LauncherStore store = new(_home);
        List<string> requests = [];
        using HttpClient http = new(new Handler(request =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri.AbsolutePath.EndsWith("index.json") ? Index : "# Devlog\n\n**Progress**")
            };
        }));
        NewsService service = new(http, store, "team/launcher");
        IReadOnlyList<NewsPost> posts = await service.GetIndexAsync(default);
        string markdown = await service.GetPostAsync(posts[0], true, default);
        Assert.Equal("https://raw.githubusercontent.com/team/launcher/main/news/index.json", requests[0]);
        Assert.EndsWith("/main/news/newer.md", requests[1]);
        NewsService offline = new(http, store, "team/launcher");
        Assert.Equal(posts, offline.ReadCache());
        Assert.Equal(markdown, await offline.GetPostAsync(posts[0], false, default));
        Assert.Equal(2, requests.Count);
        Assert.Empty(new NewsService(http, store, "other/launcher").ReadCache());
        await Assert.ThrowsAsync<IOException>(() => offline.GetPostAsync(posts[1], false, default));
    }

    [Fact]
    public async Task NetworkFailureUsesCachedArticleAndInvalidIndexPreservesCache()
    {
        LauncherStore store = new(_home);
        string response = Index;
        bool fail = false;
        using HttpClient http = new(new Handler(_ => fail
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) }));
        NewsService service = new(http, store, "team/launcher");
        IReadOnlyList<NewsPost> posts = await service.GetIndexAsync(default);
        response = "# Cached showcase";
        await service.GetPostAsync(posts[0], true, default);
        fail = true;
        Assert.Equal(response, await service.GetPostAsync(posts[0], true, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetIndexAsync(default));
        Assert.Equal(posts, service.ReadCache());
        fail = false;
        response = "[null]";
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetIndexAsync(default));
        Assert.Equal(posts, service.ReadCache());
    }

    [Fact]
    public async Task LocalDevelopmentReadsEditsAndImagesWithoutNetworkOrRemoteCache()
    {
        string local = Path.Combine(_home, "local");
        Directory.CreateDirectory(Path.Combine(local, "images"));
        await File.WriteAllTextAsync(Path.Combine(local, "index.json"), Index);
        await File.WriteAllTextAsync(Path.Combine(local, "newer.md"), "# Local article");
        await File.WriteAllBytesAsync(Path.Combine(local, "images", "test.png"), [1, 2, 3]);
        using HttpClient http = new(new Handler(_ => throw new InvalidOperationException("Local reading must not use HTTP.")));
        NewsService service = new(http, new LauncherStore(_home), "team/launcher", local);
        Assert.True(service.IsLocal);
        IReadOnlyList<NewsPost> posts = await service.GetIndexAsync(default);
        Assert.Equal(posts, service.ReadCache());
        Assert.Equal("# Local article", await service.GetPostAsync(posts[0], false, default));
        await File.WriteAllTextAsync(Path.Combine(local, "newer.md"), "# Edited article");
        Assert.Equal("# Edited article", await service.GetPostAsync(posts[0], false, default));
        Uri image = service.ImageSource(posts[0], "images/test.png");
        Assert.Equal(new byte[] { 1, 2, 3 }, await service.GetImageAsync(image, false, default));
        Assert.Throws<InvalidDataException>(() => service.ImageSource(posts[0], "../outside.png"));
        Assert.False(Directory.Exists(Path.Combine(_home, "News")));
        await File.WriteAllTextAsync(Path.Combine(local, "index.json"), "[]");
        Assert.Empty(await service.GetIndexAsync(default));
    }

    [Fact]
    public async Task RelativeRemoteImagesResolveToRawRepositoryAndAreCachedOffline()
    {
        int requests = 0;
        using HttpClient http = new(new Handler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([4, 5, 6]) };
        }));
        NewsService service = new(http, new LauncherStore(_home), "team/launcher");
        NewsPost post = NewsService.ParseIndex(Index)[0];
        Uri image = service.ImageSource(post, "images/test.png");
        Assert.Equal("https://raw.githubusercontent.com/team/launcher/main/news/images/test.png", image.AbsoluteUri);
        Assert.Equal(new byte[] { 4, 5, 6 }, await service.GetImageAsync(image, true, default));
        Assert.Equal(new byte[] { 4, 5, 6 }, await service.GetImageAsync(image, false, default));
        Assert.Equal(1, requests);
        Assert.Throws<InvalidDataException>(() => service.ImageSource(post, "file:///private/image.png"));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetImageAsync(image, false, cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, true);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
