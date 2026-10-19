using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.Markdown;
using Prowl.Rosetta;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color=System.Drawing.Color;
using TextAlignment=Prowl.PaperUI.TextAlignment;

namespace Prowl.Launcher;

/// <summary>The launcher's styled building blocks, shared by every view.</summary>
sealed class Controls(LauncherAppearance appearance, FontFile font, FontFile bold, OperationCoordinator operations, Func<Float2> screenSize)
{
    public LauncherAppearance Appearance => appearance;
    public FontFile Font => font;
    public FontFile Bold => bold;
    public OperationCoordinator Operations => operations;
    public bool Busy => operations.Busy;
    public Color Ink => appearance.Ink;
    public Color Muted => appearance.Muted;

    /// <summary>The window size in modal layout units.</summary>
    public Float2 ScreenSize => screenSize();

    /// <summary>Width available to page content: the window minus the sidebar and the content padding.</summary>
    public static float ContentWidth(Paper p) => p.ScreenRect.Size.X - Constants.Layout.SidebarWidth - 56;

    /// <summary>Wide layouts put card info and actions side by side; narrow ones stack them.</summary>
    public static bool IsWideLayout(Paper p) => ContentWidth(p) >= 800;

    /// <summary>
    ///     Draws controls inside a dialog. Origami 3.6.7 uses absolute overlay layers for dropdowns, so their layers
    ///     are rebased above the containing dialog so both the menu and its backdrop work.
    /// </summary>
    public static void AboveParentLayer(Paper p, Action draw)
    {
        int parentLayer = 0;
        for (ElementHandle parent = p.CurrentParent; parent.IsValid; parent = parent.GetParentHandle())
        {
            parentLayer = Math.Max(parentLayer, parent.Data.Layer);
        }
        int firstElement = p.ElementCount;
        draw();
        for (int i = firstElement; i < p.ElementCount; i++)
        {
            ref ElementData element = ref p.GetElementData(i);
            if (element.Layer > 0)
            {
                element.Layer += parentLayer;
            }
        }
    }

    public MarkdownBuilder StyleMarkdown(MarkdownBuilder markdown) =>
        markdown.Colors(Ink, Muted, appearance.Accent, appearance.Panel, appearance.Theme.BorderSoft);

    /// <summary>
    ///     Fills the current element with <paramref name="texture" />, scaled to cover it and centered (cropping the
    ///     overflow), clipped to the given corner radii.
    /// </summary>
    public static void DrawCoverImage(Paper p, TextureTK texture, float topLeft, float topRight, float bottomRight, float bottomLeft) => p.Draw((canvas, rect) =>
    {
        float width = rect.Size.X, height = rect.Size.Y;
        float scale = Math.Max(width / texture.Width, height / texture.Height);
        float imageWidth = texture.Width * scale, imageHeight = texture.Height * scale;
        float x = rect.Min.X, y = rect.Min.Y;
        canvas.SaveState();
        canvas.SetBrushTexture(texture);
        canvas.SetBrushTextureTransform(
            Transform2D.CreateTranslation(x + (width - imageWidth) / 2, y + (height - imageHeight) / 2)
            * Transform2D.CreateScale(imageWidth, imageHeight));
        canvas.RoundedRectFilled(x, y, width, height, topLeft, topRight, bottomRight, bottomLeft, Color.White);
        canvas.RestoreState();
    });

    public void Label(Paper p, string id, string text, float size, Color color, float height, bool bold = false, FontQuality? quality = null)
    {
        float fontSize = Math.Max(16, size * 1.1f);
        p.Box(id)
            .Height(Math.Max(height, fontSize + 8))
            .IsNotInteractable()
            .Text(Loc.Get(text), bold ? Bold : Font)
            .FontSize(fontSize)
            .TextQuality(quality ?? (size >= 23 ? FontQuality.Ultra : FontQuality.Normal))
            .TextColor(color)
            .Alignment(TextAlignment.MiddleLeft);
    }

    /// <summary>A button that runs <paramref name="action" /> as the foreground operation.</summary>
    public void Button(
        Paper p,
        string id,
        string text,
        Func<CancellationToken, Task> action,
        bool primary = false,
        float width = 100
    ) => Origami.Button(p, id, Loc.Get(text))
        .Width(width * 1.15f + 16)
        .Height(40)
        .Rounding(appearance.Theme.Metrics.Rounding)
        .LeadingIcon(LauncherIcons.ForAction(text))
        .Variant(primary ? OrigamiVariant.Primary : OrigamiVariant.Default)
        .Disabled(Busy)
        .OnClick(() => operations.Start(action, text))
        .Show();

    public IDisposable Card(Paper p, string id) => p.Column(id)
        .Height(UnitValue.Auto)
        .Padding(18)
        .Gap(8)
        .BackgroundColor(appearance.Card)
        .BorderColor(appearance.Theme.BorderSoft)
        .BorderWidth(1)
        .Rounded(appearance.Theme.Metrics.ContainerRounding)
        .Enter();

    public IDisposable VersionCard(Paper p, string id) => p.Column(id)
        .Height(UnitValue.Auto)
        .Padding(14)
        .Gap(6)
        .BackgroundColor(appearance.Card)
        .BorderColor(appearance.Theme.BorderSoft)
        .BorderWidth(1)
        .Rounded(appearance.Theme.Metrics.ContainerRounding)
        .Enter();
}
