using System.Diagnostics;

using Prowl.Aperture;
using Prowl.OrigamiUI;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private int _frames;

    private void ShowPreviewChangelog()
    {
        if (screenshot != null && notesPreview && _releases.FirstOrDefault() is {} previewRelease)
        {
            ShowChangelog(previewRelease);
        }
    }

    private bool StartPreviewOperation()
    {
        if (screenshot == null)
        {
            return false;
        }

        if (progressPreview)
        {
            _operation = new CancellationTokenSource();
            _immediateProgress = true;
            _operationTitle = Loc.Get("launcher.versions.installing", new
            {
                version = "v1.0-preview-4"
            });
            _operationDetail = Loc.Get("launcher.download.progress", new
            {
                name = Loc.Get("launcher.common.editor"),
                progress = Loc.Get("launcher.download.transferred", new
                {
                    received = "21.0", total = "50.0"
                })
            });
            _fraction = 0.42;
            return true;
        }

        if (alertPreview)
        {
            Start(PreviewUpdateAlertAsync, "launcher.updates.checking");
            return true;
        }

        if (uninstallPreview && _installed.FirstOrDefault() is {} previewEditor)
        {
            Start(token => UninstallEditorAsync(previewEditor, token), "launcher.versions.uninstall");
            return true;
        }

        return false;
    }

    private async Task PreviewUpdateAlertAsync(CancellationToken token)
    {
        await Task.Delay(100, token);
        Notify("launcher.updates.up_to_date", "launcher.updates.launcher_no_updates");
    }

    private void CaptureScreenshot()
    {
        if (screenshot == null || ++_frames < 30 || _fpsText == "— FPS")
        {
            return;
        }

        // Capture confirmation previews after the normal progress delay has elapsed.
        if (uninstallPreview && Stopwatch.GetElapsedTime(_operationStarted).TotalSeconds < 1)
        {
            return;
        }

        int width = _window.FramebufferSize.X, height = _window.FramebufferSize.Y;
        byte[] pixels = new byte[width * height * 4];
        OpenTK.Graphics.OpenGL4.GL.ReadPixels(0, 0, width, height,
            OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, pixels);
        byte[] flipped = new byte[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(pixels, y * width * 4, flipped, (height - 1 - y) * width * 4, width * 4);
        }

        using Image image = Aperture.Image.FromPixels(flipped, width, height, Aperture.PixelFormat.Rgba8);
        image.Save(screenshot);
        ForceClose();
    }
}
