using OpenTK.Graphics.OpenGL4;

using Prowl.Aperture;
using Prowl.Rosetta;

using PixelFormat=OpenTK.Graphics.OpenGL4.PixelFormat;
namespace Prowl.Launcher;

/// <summary>Screenshot capture and the preview states it can open into.</summary>
public sealed partial class Launcher
{
    private int _frames;

    private void ShowPreviewChangelog()
    {
        if (Capturing && _previews.Notes && _editors.Releases.FirstOrDefault() is {} previewRelease)
        {
            _dialogs.ShowChangelog(previewRelease);
        }
    }

    private bool StartPreviewOperation()
    {
        if (!Capturing)
        {
            return false;
        }

        if (_previews.Progress)
        {
            _operations.ShowPreview(
                Loc.Get("launcher.versions.installing", new
                {
                    version = "v1.0-preview-4"
                }),
                Loc.Get("launcher.download.progress", new
                {
                    name = Loc.Get("launcher.common.editor"),
                    progress = Loc.Get("launcher.download.transferred", new
                    {
                        received = "21.0", total = "50.0"
                    })
                }),
                0.42);
            return true;
        }

        if (_previews.Alert)
        {
            _operations.Start(PreviewUpdateAlertAsync, "launcher.updates.checking");
            return true;
        }

        if (_previews.Uninstall && _editors.Installed.FirstOrDefault() is {} previewEditor)
        {
            _operations.Start(token => _editors.UninstallAsync(previewEditor, token), "launcher.versions.uninstall");
            return true;
        }

        return false;
    }

    private async Task PreviewUpdateAlertAsync(CancellationToken token)
    {
        await Task.Delay(100, token);
        _notifications.Notify("launcher.updates.up_to_date", "launcher.updates.launcher_no_updates");
    }

    private void CaptureScreenshot()
    {
        if (!Capturing || ++_frames < 30 || !_titleBar.HasFrameRate)
        {
            return;
        }

        // Capture confirmation previews after the normal progress delay has elapsed.
        if (_previews.Uninstall && _operations.Elapsed.TotalSeconds < 1)
        {
            return;
        }

        int width = _window.FramebufferSize.X, height = _window.FramebufferSize.Y;
        byte[] pixels = new byte[width * height * 4];
        GL.ReadPixels(0, 0, width, height,
            PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        byte[] flipped = new byte[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(pixels, y * width * 4, flipped, (height - 1 - y) * width * 4, width * 4);
        }

        using Image image = Image.FromPixels(flipped, width, height, Aperture.PixelFormat.Rgba8);
        image.Save(screenshot!);
        ForceClose();
    }
}
