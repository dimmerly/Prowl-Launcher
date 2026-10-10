using Prowl.Aperture;

namespace Prowl.Launcher;

// Downloads happen asynchronously; textures are uploaded by Get on the render thread.
sealed class NewsImages : IDisposable
{
    private readonly Dictionary<string, Task<byte[]>> _downloads = [];
    private readonly Dictionary<string, TextureTK?> _textures = [];

    internal TextureTK? Get(NewsService service, NewsPost post, string href, bool allowNetwork,
        CancellationToken token, Action<Exception> log)
    {
        string key = post.File + ":" + href;
        if (_textures.TryGetValue(key, out TextureTK? texture))
        {
            return texture;
        }

        try
        {
            if (!_downloads.TryGetValue(key, out Task<byte[]>? download))
            {
                download = service.GetImageAsync(service.ImageSource(post, href), allowNetwork, token);
                _downloads.Add(key, download);
            }
            if (!download.IsCompleted)
            {
                return null;
            }

            using MemoryStream stream = new( download.GetAwaiter().GetResult() );
            using Image decoded = Image.Load(stream, new DecodeOptions
            {
                TargetPixelFormat = PixelFormat.Rgba8
            });
            texture = TextureTK.FromImage(decoded);
            _textures.Add(key, texture);
            return texture;
        }
        catch (Exception error)
        {
            _textures[key] = null;
            if (!token.IsCancellationRequested)
            {
                log(error);
            }

            return null;
        }
    }

    public void Dispose()
    {
        foreach (TextureTK? texture in _textures.Values)
        {
            texture?.Dispose();
        }

        _textures.Clear();
        _downloads.Clear();
    }
}
