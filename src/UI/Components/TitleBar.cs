using System.Drawing;

using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>
///     The window's title bar: language picker, optional FPS counter and window controls. Without a native frame
///     (<paramref name="nativeFrame" /> false) it also drags and maximizes the window.
/// </summary>
sealed class TitleBar(LauncherStore store, Controls ui, GameWindow window, bool nativeFrame)
{
    private int _fpsFrames;
    private double _fpsSeconds;
    private string _fpsText = "— FPS";
    private OrigamiTheme? _languageTheme;
    private OrigamiTheme? _languageThemeSource;
    private Vector2i _windowDragOffset;

    /// <summary>True once enough frames have been drawn to show a frame rate.</summary>
    public bool HasFrameRate => _fpsText != "— FPS";

    /// <summary>Width of the controls on the right, which the native frame must not treat as a drag area.</summary>
    public static float ControlsWidth(Settings settings) => (settings.ShowFps ? Constants.Layout.FpsWidth : 0) + Constants.Layout.LanguagePickerWidth + 126;

    public void Draw(Paper p)
    {
        using (p.Row("title-bar")
            .PositionType(PositionType.SelfDirected)
            .Left(0)
            .Top(0)
            .Width(p.ScreenRect.Size.X)
            .Height(Constants.Layout.TitleBarHeight)
            .Enter())
        {
            ElementBuilder dragArea = p.Box("window-drag-area").Height(Constants.Layout.TitleBarHeight);
            if (!nativeFrame)
            {
                dragArea.OnDoubleClick(_ => ToggleMaximized())
                    .OnDragStart(_ => _windowDragOffset = new Vector2i((int)window.MousePosition.X, (int)window.MousePosition.Y))
                    .OnDragging(_ =>
                    {
                        if (window.WindowState == WindowState.Normal)
                        {
                            window.Location += new Vector2i((int)window.MousePosition.X, (int)window.MousePosition.Y) - _windowDragOffset;
                        }
                    });
            }

            using (p.Row("window-controls").Width(ControlsWidth(store.Settings)).Height(Constants.Layout.TitleBarHeight).Enter())
            {
                if (store.Settings.ShowFps)
                {
                    p.Box("fps")
                        .Width(Constants.Layout.FpsWidth)
                        .Height(Constants.Layout.TitleBarHeight)
                        .Text(_fpsText, ui.Font)
                        .FontSize(16)
                        .TextColor(ui.Muted)
                        .Alignment(TextAlignment.MiddleCenter)
                        .IsNotInteractable();
                }
                DrawLanguagePicker(p);
                WindowControl(p, "minimize-window", "launcher.window.minimize", LauncherIcons.Minimize,
                    () => window.WindowState = WindowState.Minimized);
                bool maximized = window.WindowState == WindowState.Maximized;
                WindowControl(p, "maximize-window", maximized ? "launcher.window.restore" : "launcher.window.maximize",
                    maximized ? OrigamiIconSet.Duplicate : OrigamiIconSet.Expand, ToggleMaximized);
                WindowControl(p, "close-window", "launcher.common.close", OrigamiIconSet.Close, () => window.Close(), true);
            }
        }
    }

    public void CountFrame(float delta)
    {
        _fpsSeconds += delta;
        _fpsFrames++;
        if (_fpsSeconds >= 0.5)
        {
            _fpsText = $"{_fpsFrames / _fpsSeconds:F0} FPS";
            _fpsSeconds = 0;
            _fpsFrames = 0;
        }
    }

    private void DrawLanguagePicker(Paper p)
    {
        if (_languageThemeSource != ui.Appearance.Theme)
        {
            _languageThemeSource = ui.Appearance.Theme;
            _languageTheme = _languageThemeSource.Clone();
            _languageTheme.BorderStrong = Color.Transparent;
        }
        using IDisposable theme = Origami.PushTheme(_languageTheme!);
        string locale = store.Settings.Locale ?? "en";
        Origami.Dropdown(p, "title-language", locale, value =>
            {
                Loc.SetLocale(value);
                store.Settings.Locale = value;
                store.Save();
            }, LocaleHelper.Codes)
            .Subtle()
            .Width(Constants.Layout.LanguagePickerWidth)
            .Height(Constants.Layout.TitleBarHeight)
            .PopoverWidth(210)
            .ItemHeight(30)
            .Display(code => LocaleHelper.Names[Array.IndexOf(LocaleHelper.Codes, code)])
            .CustomTrigger(context =>
            {
                Color color = p.IsParentHovered || context.IsOpen ? ui.Ink : ui.Muted;
                p.Box("language-globe").Width(20).Margin(8, 4, 0, 0)
                    .IsNotInteractable().Icon(p, OrigamiIconSet.Globe, color, size: 15);
                p.Box("language-code").Width(UnitValue.StretchOne)
                    .IsNotInteractable().Text(locale.ToUpperInvariant(), ui.Font)
                    .FontSize(13).TextColor(color).Alignment(TextAlignment.MiddleCenter);
                p.Box("language-chevron").Width(16).Margin(4, 8, 0, 0)
                    .IsNotInteractable()
                    .Icon(p, context.IsOpen ? OrigamiIconSet.ChevronUp : OrigamiIconSet.ChevronDown, color, size: 10);
            })
            .Show();
    }

    private void WindowControl(Paper p, string id, string label, IOrigamiIcon icon, Action action, bool close = false)
    {
        ElementBuilder button = p.Box(id)
            .Size(42, Constants.Layout.TitleBarHeight)
            .Cursor(PaperCursor.Pointer)
            .Tooltip(Loc.Get(label))
            .OnClick(_ => action())
            .Icon(p, icon, ui.Ink, size: 16);
        button.Hovered.BackgroundColor(close ? ui.Appearance.Theme.Red.C500 : ui.Appearance.Theme.Hover);
    }

    private void ToggleMaximized() =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
