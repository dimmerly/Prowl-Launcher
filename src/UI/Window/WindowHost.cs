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
    private bool _mouseOverWindow;
    private long _lastScroll;
    private WindowScale _windowScale = new( 1, 1 );

    public void Run(string title, int width, int height)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        using UiContext context = _context = new UiContext();
        using LauncherWindow window = _window = new LauncherWindow(this, title, width, height);
        window.Run();
    }

    private unsafe void LoadWindow()
    {
        UpdateWindowScale();
        _window.VSync = VSyncMode.On;
        using (Stream iconStream = typeof(Launcher).Assembly.GetManifestResourceStream("Prowl.Launcher.prowl.png")
                                   ?? throw new InvalidDataException("The launcher icon is missing."))
        using (Prowl.Aperture.Image icon = Prowl.Aperture.Image.Load(iconStream,
                   new Prowl.Aperture.DecodeOptions { TargetPixelFormat = Prowl.Aperture.PixelFormat.Rgba8 }))
        {
            _window.Icon = new WindowIcon(new OpenTK.Windowing.Common.Input.Image(icon.Width, icon.Height, icon.Pixels.ToArray()));
        }
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
        UpdateWindowScale();
        _context.Pump();
        RefreshEditorConsoles();
        OfferPendingLauncherUpdate();
        UpdateFrameRate();
    }

    private void UpdateFrameRate()
    {
        if (_window == null)
        {
            return;
        }

        bool scrolling = _lastScroll != 0 && Stopwatch.GetElapsedTime(_lastScroll).TotalSeconds < 1;
        _window.UpdateFrequency = _window.IsFocused || _mouseOverWindow || _windowResizing || scrolling || screenshot != null ? 0 : 5;
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
        if (LauncherStartupService.ReadyPipe != null)
        {
            _ = LauncherStartupService.ReportReadyAsync();
        }
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

    private float UiScale => float.IsFinite(store.Settings.UiScale) ? Math.Clamp(store.Settings.UiScale, Constants.Defaults.MinUiScale, Constants.Defaults.MaxUiScale) : Constants.Defaults.UiScale;
    private float DisplayScale => _windowScale.Rendering;

    private unsafe void UpdateWindowScale()
    {
        GLFW.GetWindowContentScale(_window.WindowPtr, out float contentScale, out _);
        float framebufferRatio = (float)_window.FramebufferSize.X / Math.Max(1, _window.ClientSize.X);
        _windowScale = WindowScale.Calculate(UiScale, contentScale, framebufferRatio);
    }

    private void PreparePaperFrame()
    {
        UpdateWindowScale();
        _paper.SetResolution(_window.FramebufferSize.X / DisplayScale, _window.FramebufferSize.Y / DisplayScale);
        _paper.DisplayFramebufferScale = new Float2(DisplayScale, DisplayScale);
    }

    private Float2 Pointer(float x, float y) => new( x / _windowScale.Input, y / _windowScale.Input );

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
