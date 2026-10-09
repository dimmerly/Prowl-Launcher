using System.Drawing;

using Prowl.Rosetta;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using OpenTK.Mathematics;
using OpenTK.Windowing.Common;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    internal const float TitleBarHeight = 34;
    private const float LanguagePickerWidth = 86;
    private const float FpsWidth = 84;
    private float WindowControlsWidth => (store.Settings.ShowFps ? FpsWidth : 0) + LanguagePickerWidth + 126;
    private Vector2i _windowDragOffset;
    private double _fpsSeconds;
    private int _fpsFrames;
    private string _fpsText = "— FPS";
    private OrigamiTheme? _languageThemeSource;
    private OrigamiTheme? _languageTheme;

    private float WindowUiScale => _windowScale.Input;

    private void DrawTitleBar(Paper p)
    {
        using (p.Row("title-bar")
            .PositionType(PositionType.SelfDirected)
            .Left(0)
            .Top(0)
            .Width((float)p.ScreenRect.Size.X)
            .Height(TitleBarHeight)
            .Enter())
        {
            ElementBuilder dragArea = p.Box("window-drag-area").Height(TitleBarHeight);
            if (_windowFrame == null)
            {
                dragArea.OnDoubleClick(_ => ToggleMaximized())
                    .OnDragStart(_ => _windowDragOffset = new Vector2i((int)_window.MousePosition.X, (int)_window.MousePosition.Y))
                    .OnDragging(_ =>
                    {
                        if (_window.WindowState == WindowState.Normal)
                        {
                            _window.Location += new Vector2i((int)_window.MousePosition.X, (int)_window.MousePosition.Y) - _windowDragOffset;
                        }
                    });
            }

            using (p.Row("window-controls").Width(WindowControlsWidth).Height(TitleBarHeight).Enter())
            {
                if (store.Settings.ShowFps)
                {
                    p.Box("fps")
                        .Width(FpsWidth)
                        .Height(TitleBarHeight)
                        .Text(_fpsText, _font)
                        .FontSize(16)
                        .TextColor(Muted)
                        .Alignment(TextAlignment.MiddleCenter)
                        .IsNotInteractable();
                }
                DrawLanguagePicker(p);
                WindowControl(p, "minimize-window", "launcher.window.minimize", LauncherIcons.Minimize,
                    () => _window.WindowState = WindowState.Minimized);
                bool maximized = _window.WindowState == WindowState.Maximized;
                WindowControl(p, "maximize-window", maximized ? "launcher.window.restore" : "launcher.window.maximize",
                    maximized ? OrigamiIconSet.Duplicate : OrigamiIconSet.Expand, ToggleMaximized);
                WindowControl(p, "close-window", "launcher.common.close", OrigamiIconSet.Close, () => _window.Close(), true);
            }
        }
    }

    private void DrawLanguagePicker(Paper p)
    {
        if (_languageThemeSource != _appearance.Theme)
        {
            _languageThemeSource = _appearance.Theme;
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
            .Width(LanguagePickerWidth)
            .Height(TitleBarHeight)
            .PopoverWidth(210)
            .ItemHeight(30)
            .Display(code => LocaleHelper.Names[Array.IndexOf(LocaleHelper.Codes, code)])
            .CustomTrigger(context =>
            {
                Color color = p.IsParentHovered || context.IsOpen ? Ink : Muted;
                p.Box("language-globe").Width(20).Margin(8, 4, 0, 0)
                    .IsNotInteractable().Icon(p, OrigamiIconSet.Globe, color, size: 15);
                p.Box("language-code").Width(UnitValue.StretchOne)
                    .IsNotInteractable().Text(locale.ToUpperInvariant(), _font)
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
            .Size(42, TitleBarHeight)
            .Cursor(PaperCursor.Pointer)
            .Tooltip(Loc.Get(label))
            .OnClick(_ => action())
            .Icon(p, icon, Ink, size: 16);
        button.Hovered.BackgroundColor(close ? _appearance.Theme.Red.C500 : _appearance.Theme.Hover);
    }

    private void ToggleMaximized()
    {
        LauncherWindow window = _window;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateFps(float delta)
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
}
