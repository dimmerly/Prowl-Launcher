using System.Diagnostics;
using Prowl.Aperture;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Samples;

internal static class SamplePreviewHelper
{
    public static void Capture(string path)
    {
        long? started = null;
        Window.PostRender += _ =>
        {
            started ??= Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(started.Value).TotalSeconds < 2.5)
                return;
            using Texture2D texture = Graphics.Screenshot();
            int width = (int)texture.Width, height = (int)texture.Height;
            byte[] pixels = new byte[width * height * 4];
            texture.GetData<byte>(pixels);
            byte[] flipped = new byte[pixels.Length];
            for (int y = 0; y < height; y++)
                Array.Copy(pixels, y * width * 4, flipped, (height - 1 - y) * width * 4, width * 4);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using Image image = Image.FromPixels(flipped, width, height, PixelFormat.Rgba8);
            image.Save(path);
            Window.Stop(force: true);
        };
    }
}
