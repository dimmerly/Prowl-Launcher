using Prowl.Aperture;

namespace Prowl.Launcher;

sealed class SampleThumbnail : IDisposable
{
    private readonly Dictionary<string, TextureTK> _textures = [];

    public TextureTK? Get(Sample sample)
    {
        if (_textures.TryGetValue(sample.Id, out TextureTK? texture))
        {
            return texture;
        }
        using Stream? image = typeof( Launcher ).Assembly.GetManifestResourceStream($"Prowl.Launcher.Samples.{sample.Id}.png");
        if (image == null)
        {
            return null;
        }
        using Image decoded = Image.Load(image, new DecodeOptions
        {
            TargetPixelFormat = PixelFormat.Rgba8
        });
        texture = TextureTK.FromImage(decoded);
        _textures.Add(sample.Id, texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (TextureTK texture in _textures.Values)
        {
            texture.Dispose();
        }
        _textures.Clear();
    }
}
