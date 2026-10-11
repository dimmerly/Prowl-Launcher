using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Prowl.Launcher;

sealed class NewsService(HttpClient http, LauncherStore store, string repository, string? localDirectory = null)
{
    private const string ArticlePathPattern = @"\A(?:[a-zA-Z0-9][a-zA-Z0-9_-]*/)*[a-zA-Z0-9][a-zA-Z0-9_-]*\.md\z";
    private static readonly JsonSerializerOptions Options = new( JsonSerializerDefaults.Web );

    private string CacheRoot => Path.Combine(store.Home, "News", _repository.Replace('/', '_'));
    private string IndexPath => Path.Combine(_localDirectory ?? CacheRoot, "index.json");

    internal bool IsLocal => _localDirectory != null;
    internal DateTime? LocalIndexStamp => IsLocal ? File.GetLastWriteTimeUtc(IndexPath) : null;
    private readonly string? _localDirectory = localDirectory == null ? null : Path.GetFullPath(localDirectory);
    private readonly string _repository = GitHubRepository.Normalize(repository);

    internal static string? DevelopmentDirectory() => typeof( NewsService ).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "DevelopmentNewsDirectory")?.Value;

    internal Uri Source(string file) => new( $"https://raw.githubusercontent.com/{_repository}/main/news/{file}" );
    internal Uri Page(string file) => new( $"https://github.com/{_repository}/blob/main/news/{file}" );

    internal static IReadOnlyList<NewsPost> ParseIndex(string json)
    {
        NewsPost[] posts = JsonSerializer.Deserialize<NewsPost[]>(json, Options)
                           ?? throw new InvalidDataException("The news index must be an array.");
        HashSet<string> files = new( StringComparer.OrdinalIgnoreCase );
        foreach (NewsPost? post in posts)
        {
            ValidatePost(post);
            if (!files.Add(post.File))
            {
                throw new InvalidDataException("The news index contains an invalid or duplicate post.");
            }
        }
        return posts.OrderByDescending(post => post.Date)
            .ThenBy(post => post.File, StringComparer.Ordinal)
            .ToArray();
    }

    internal IReadOnlyList<NewsPost> ReadCache()
    {
        try
        {
            return ParseIndex(File.ReadAllText(IndexPath));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return [];
        }
    }

    internal async Task<IReadOnlyList<NewsPost>> GetIndexAsync(CancellationToken token)
    {
        if (_localDirectory != null)
        {
            return ParseIndex(await File.ReadAllTextAsync(IndexPath, token));
        }
        string json = await DownloadAsync("index.json", token);
        IReadOnlyList<NewsPost> posts = ParseIndex(json);
        JsonFile.Write(IndexPath, posts);
        return posts;
    }

    internal async Task<string> GetPostAsync(NewsPost post, bool allowNetwork, CancellationToken token)
    {
        ValidatePost(post);
        if (_localDirectory != null)
        {
            return await File.ReadAllTextAsync(Path.Combine(_localDirectory, post.File), token);
        }
        string cachePath = Path.Combine(CacheRoot, post.File + ".json");
        if (!allowNetwork)
        {
            return JsonFile.Read<string>(cachePath)
                   ?? throw new IOException("This news article has not been cached for offline reading.");
        }

        try
        {
            string markdown = await DownloadAsync(post.File, token);
            JsonFile.Write(cachePath, markdown);
            return markdown;
        }
        catch (Exception error) when (error is HttpRequestException
                                      || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            if (JsonFile.Read<string>(cachePath) is {} cached)
            {
                return cached;
            }
            throw;
        }
    }

    internal Uri ImageSource(NewsPost post, string href)
    {
        Uri article = IsLocal ? new Uri(Path.Combine(_localDirectory!, post.File)) : Source(post.File);
        Uri source = new( article, href );
        if (IsLocal && source.IsFile)
        {
            LocalImagePath(source);
            return source;
        }
        if (source.Scheme != "https")
        {
            throw new InvalidDataException("News images must use HTTPS or local news assets.");
        }
        return source;
    }

    internal async Task<byte[]> GetImageAsync(Uri source, bool allowNetwork, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsLocal && source.IsFile)
        {
            return await File.ReadAllBytesAsync(LocalImagePath(source), token);
        }
        if (source.Scheme != "https")
        {
            throw new InvalidDataException("News images must use HTTPS.");
        }
        string cachePath = ImageCachePath(source);
        if (JsonFile.Read<byte[]>(cachePath) is {} cached)
        {
            return cached;
        }
        if (!allowNetwork)
        {
            throw new IOException("This news image has not been cached for offline reading.");
        }
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(Constants.Network.ReleaseCheckTimeout);
        byte[] bytes = await http.GetByteArrayAsync(source, deadline.Token);
        JsonFile.Write(cachePath, bytes);
        return bytes;
    }

    private static void ValidatePost([NotNull] NewsPost? post)
    {
        if (post == null || string.IsNullOrWhiteSpace(post.Title) || post.Date == default
            || post.File == null || !Regex.IsMatch(post.File, ArticlePathPattern))
        {
            throw new InvalidDataException("The news index contains an invalid or duplicate post.");
        }
    }

    private string LocalImagePath(Uri source) => FileSystemHelper.SafeChildPath(
        _localDirectory!, Path.GetRelativePath(_localDirectory!, source.LocalPath));

    private string ImageCachePath(Uri source)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source.AbsoluteUri));
        return Path.Combine(CacheRoot, "images", Convert.ToHexString(hash) + ".json");
    }

    private async Task<string> DownloadAsync(string file, CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(Constants.Network.ReleaseCheckTimeout);
        return await http.GetStringAsync(Source(file), deadline.Token);
    }
}
