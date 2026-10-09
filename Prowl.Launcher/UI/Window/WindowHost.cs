using System.Globalization;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private LauncherWindow _window = null!;
    private PaperRenderer _renderer = null!;
    private Paper _paper = null!;
    private WindowFrame? _windowFrame;
    private UiContext _context = null!;
    private float _deltaTime;
    private bool _forceClose;
    private bool _windowResizing;
    private long _lastScroll;

    public void Run(string title, int width, int height)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        using UiContext context = _context = new();
        using LauncherWindow window = _window = new(this, title, width, height);
        window.Run();
    }

    private unsafe void LoadWindow()
    {
        _window.VSync = VSyncMode.On;
        if (OperatingSystem.IsWindows())
        {
            _windowFrame = new WindowFrame(
                GLFW.GetWin32Window(_window.WindowPtr),
                _window.ClientSize.X,
                _window.ClientSize.Y,
                () => WindowUiScale,
                () => WindowControlsWidth,
                resizing =>
                {
                    _windowResizing = resizing;
                    UpdateFrameRate();
                });
        }
        else
        {
            _window.WindowBorder = WindowBorder.Hidden;
        }

        _renderer = new PaperRenderer();
        _renderer.Initialize(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        _paper = new Paper(_renderer, _window.ClientSize.X, _window.ClientSize.Y, new Quill.FontAtlasSettings());
        _paper.SetClipboardHandler(new ClipboardHandler(_window));
        _paper.OnCursorChange += cursor => _window.Cursor = MapCursor(cursor);
        Initialize();
        UpdateFrameRate();
    }

    private void UpdateWindow(float delta)
    {
        _deltaTime = delta;
        _context.Pump();
        UpdateFrameRate();
    }

    private void UpdateFrameRate()
    {
        if (_window == null)
        {
            return;
        }

        bool scrolling = _lastScroll != 0 && Stopwatch.GetElapsedTime(_lastScroll).TotalSeconds < 1;
        _window.UpdateFrequency = _window.IsFocused || _windowResizing || scrolling || screenshot != null ? 0 : 5;
    }

    private void RenderWindow(float delta)
    {
        UpdateFps(delta);
        GL.Viewport(0, 0, _window.FramebufferSize.X, _window.FramebufferSize.Y);
        GL.ClearColor(0, 0, 0, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
        PreparePaperFrame();
        _paper.BeginFrame(delta, -1);
        OnGui(_paper);
        _paper.EndFrame();
        CaptureScreenshot();
    }

    private void CloseWindow()
    {
        _windowFrame?.Dispose();
        Closing();
        _renderer.Dispose();
    }

    private void ForceClose()
    {
        _forceClose = true;
        _window.Close();
    }

    private void CenterWindow() => _window.CenterWindow();

    private float UiScale => float.IsFinite(store.Settings.UiScale) ? Math.Clamp(store.Settings.UiScale, 0.5f, 1.5f) : 1;
    private float DisplayScale => Math.Max(0.01f, (float)_window.FramebufferSize.X / Math.Max(1, _window.ClientSize.X)) * UiScale;

    private void PreparePaperFrame()
    {
        _paper.SetResolution(_window.FramebufferSize.X / DisplayScale, _window.FramebufferSize.Y / DisplayScale);
        _paper.DisplayFramebufferScale = new Float2(DisplayScale, DisplayScale);
    }

    private Float2 Pointer(float x, float y) => new(x / UiScale, y / UiScale);

    private bool HandleCloseRequested()
    {
        if (!Busy)
        {
            return true;
        }

        _closeAfterCancel = true;
        _operation?.Cancel();
        return false;
    }

    private sealed class ClipboardHandler(LauncherWindow window) : IClipboardHandler
    {
        public string GetClipboardText() => window.ClipboardString ?? "";
        public void SetClipboardText(string text) => window.ClipboardString = text;
    }

    private static MouseCursor MapCursor(PaperCursor cursor) => cursor switch
    {
        PaperCursor.Pointer => MouseCursor.PointingHand,
        PaperCursor.Text => MouseCursor.IBeam,
        PaperCursor.Crosshair => MouseCursor.Crosshair,
        PaperCursor.ResizeHorizontal => MouseCursor.ResizeEW,
        PaperCursor.ResizeVertical => MouseCursor.ResizeNS,
        PaperCursor.ResizeNWSE => MouseCursor.ResizeNWSE,
        PaperCursor.ResizeNESW => MouseCursor.ResizeNESW,
        PaperCursor.ResizeAll => MouseCursor.ResizeAll,
        PaperCursor.NotAllowed => MouseCursor.NotAllowed,
        _ => MouseCursor.Default
    };
}
