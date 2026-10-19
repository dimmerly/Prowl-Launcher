using System.Drawing;

using Prowl.Launcher.Theming;
using Prowl.OrigamiUI;
using Prowl.Scribe;

namespace Prowl.Launcher;

/// <summary>Uses the editor's theme data and presets, with independent launcher settings.</summary>
internal sealed class LauncherAppearance
{
    private readonly FontFile _bold;
    private readonly FontFile _font;
    private readonly string _path;

    public LauncherAppearance(string home, FontFile font, FontFile bold)
    {
        _path = Path.Combine(home, "theme.json");
        _font = font;
        _bold = bold;
        Data = EditorThemeData.ImportFromFile(_path) ?? ReadEditorTheme();
        Apply();
    }

    public EditorThemeData Data
    {
        get;
        private set;
    }

    public OrigamiTheme Theme
    {
        get;
        private set;
    } = null!;

    public Color Ink => Theme.Ink.C500;
    public Color Muted => Theme.Ink.C300;
    public Color Accent => Theme.Primary.C500;

    public Color Background => ColorRamp.ParseHex(Data.BackgroundStyle == EditorBackgroundStyle.Color
        ? Data.BackgroundColorA
        : Data.BackgroundVoidColor);

    public Color Panel => Opaque(Theme.Neutral.C500);
    public Color Card => Opaque(Theme.Neutral.C300);

    public void UseEditorTheme()
    {
        Data = ReadEditorTheme();
        Save();
    }

    public void UsePreset(string name)
    {
        ThemePresets.All.First(p => p.Name == name).ApplyTo(Data);
        Save();
    }

    public void ResetToDefault()
    {
        Data = EditorThemeData.CreateDefault();
        Save();
    }

    public void Save()
    {
        Apply();
        JsonFile.Write(_path, Data);
    }

    private void Apply()
    {
        Data.InitRamps();
        Theme = OrigamiTheme.CreateDefaults();
        Data.ApplyTo(Theme);
        Theme.Metrics.Rounding = Data.Roundness;
        Theme.Metrics.ContainerRounding = Data.Roundness > 0f ? Data.Roundness + 2f : 0f;
        Theme.Metrics.SmallRounding = Data.Roundness * 0.5f;
        Theme.Font = Theme.FontMedium = _font;
        Theme.FontSemiBold = Theme.FontBold = _bold;
        Theme.Metrics.FontSize = 18;
        Theme.Metrics.FontSizeSmall = 16;
        Theme.Metrics.RowHeight = 40;
        Theme.Metrics.HeaderHeight = 32;
        Theme.Metrics.IconWidth = 20;
    }

    private static EditorThemeData ReadEditorTheme() => EditorSettings.Theme() ?? EditorThemeData.CreateDefault();

    public static Color Opaque(Color color) => Color.FromArgb(255, color.R, color.G, color.B);
}
