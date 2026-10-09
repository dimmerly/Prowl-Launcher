using System.ComponentModel;
using System.Diagnostics;

using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
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
            owner._paper?.SetPointerState(PaperMouseBtn.Unknown, point.X, point.Y, false, true);
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
            owner._paper?.SetPointerState(paperButton, point.X, point.Y, down, false);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs args)
        {
            base.OnMouseWheel(args);
            owner._lastScroll = Stopwatch.GetTimestamp();
            owner._paper?.SetPointerWheel(args.OffsetY);
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
                owner._paper?.SetKeyState(paperKey, down);
        }

        protected override void OnTextInput(TextInputEventArgs args)
        {
            base.OnTextInput(args);
            owner._paper?.AddInputCharacter(args.AsString);
        }
    }
}
