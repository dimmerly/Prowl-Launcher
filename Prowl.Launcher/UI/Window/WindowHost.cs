using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
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
    private Paper PaperInstance = null!;
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
            _windowFrame = new WindowFrame(GLFW.GetWin32Window(_window.WindowPtr), _window.ClientSize.X, _window.ClientSize.Y,
                () => WindowUiScale, () => WindowControlsWidth, resizing => { _windowResizing = resizing; UpdateFrameRate(); });
        else
            _window.WindowBorder = WindowBorder.Hidden;

        _renderer = new PaperRenderer();
        _renderer.Initialize(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        PaperInstance = new Paper(_renderer, _window.ClientSize.X, _window.ClientSize.Y, new Quill.FontAtlasSettings());
        PaperInstance.SetClipboardHandler(new ClipboardHandler(_window));
        PaperInstance.OnCursorChange += cursor => _window.Cursor = MapCursor(cursor);
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
            return;
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
        PaperInstance.BeginFrame(delta, -1);
        OnGui(PaperInstance);
        PaperInstance.EndFrame();
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
    private float UiScale => float.IsFinite(store.Settings.UiScale) ? Math.Clamp(store.Settings.UiScale, 0.9f, 1.5f) : 1;
    private float DisplayScale => Math.Max(0.01f, (float)_window.FramebufferSize.X / Math.Max(1, _window.ClientSize.X)) * UiScale;

    private void PreparePaperFrame()
    {
        PaperInstance.SetResolution(_window.FramebufferSize.X / DisplayScale, _window.FramebufferSize.Y / DisplayScale);
        PaperInstance.DisplayFramebufferScale = new Float2(DisplayScale, DisplayScale);
    }

    private Float2 Pointer(float x, float y) => new(x / UiScale, y / UiScale);

    private bool HandleCloseRequested()
    {
        if (!Busy)
            return true;
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

    private sealed class LauncherWindow(Launcher owner, string title, int width, int height) : GameWindow(
        GameWindowSettings.Default,
        new NativeWindowSettings
        {
            Title = title,
            ClientSize = new Vector2i(width, height),
            StartVisible = false,
            APIVersion = OperatingSystem.IsMacOS() ? new Version(4, 1) : new Version(3, 3),
            Flags = ContextFlags.ForwardCompatible,
            Profile = ContextProfile.Core
        })
    {
        protected override void OnLoad()
        {
            base.OnLoad();
            owner.LoadWindow();
        }
        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            owner.UpdateWindow((float)args.Time);
        }
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            owner.RenderWindow((float)args.Time);
            SwapBuffers();
        }
        protected override void OnFramebufferResize(FramebufferResizeEventArgs args)
        {
            base.OnFramebufferResize(args);
            owner._renderer?.UpdateProjection(args.Width, args.Height);
        }
        protected override void OnClosing(CancelEventArgs args)
        {
            args.Cancel = !owner._forceClose && !owner.HandleCloseRequested();
            base.OnClosing(args);
        }
        protected override void OnUnload()
        {
            owner.CloseWindow();
            base.OnUnload();
        }
        protected override void OnFocusedChanged(FocusedChangedEventArgs args)
        {
            base.OnFocusedChanged(args);
            owner.UpdateFrameRate();
        }
        protected override void OnMouseMove(MouseMoveEventArgs args)
        {
            base.OnMouseMove(args);
            Float2 point = owner.Pointer(args.X, args.Y);
            owner.PaperInstance?.SetPointerState(PaperMouseBtn.Unknown, point.X, point.Y, false, true);
        }
        protected override void OnMouseDown(MouseButtonEventArgs args)
        {
            base.OnMouseDown(args);
            SetButton(args.Button, true);
        }
        protected override void OnMouseUp(MouseButtonEventArgs args)
        {
            base.OnMouseUp(args);
            SetButton(args.Button, false);
        }
        private void SetButton(MouseButton button, bool down)
        {
            Float2 point = owner.Pointer(MousePosition.X, MousePosition.Y);
            PaperMouseBtn paperButton = button == MouseButton.Left ? PaperMouseBtn.Left : button == MouseButton.Right ? PaperMouseBtn.Right : PaperMouseBtn.Middle;
            owner.PaperInstance?.SetPointerState(paperButton, point.X, point.Y, down, false);
        }
        protected override void OnMouseWheel(MouseWheelEventArgs args)
        {
            base.OnMouseWheel(args);
            owner._lastScroll = Stopwatch.GetTimestamp();
            owner.PaperInstance?.SetPointerWheel(args.OffsetY);
            owner.UpdateFrameRate();
        }
        protected override void OnKeyDown(KeyboardKeyEventArgs args)
        {
            base.OnKeyDown(args);
            SetKey(args.Key, true);
        }
        protected override void OnKeyUp(KeyboardKeyEventArgs args)
        {
            base.OnKeyUp(args);
            SetKey(args.Key, false);
        }
        private void SetKey(Keys key, bool down)
        {
            string name = key.ToString();
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
                name = "Num" + name[1];
            if (Enum.TryParse(name, out PaperKey paperKey))
                owner.PaperInstance?.SetKeyState(paperKey, down);
        }
        protected override void OnTextInput(TextInputEventArgs args)
        {
            base.OnTextInput(args);
            owner.PaperInstance?.AddInputCharacter(args.AsString);
        }
    }
}
