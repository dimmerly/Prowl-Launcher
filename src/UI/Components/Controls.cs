using Prowl.Rosetta;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color=System.Drawing.Color;
using TextAlignment=Prowl.PaperUI.TextAlignment;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private Color Ink => _appearance.Ink;
    private Color Muted => _appearance.Muted;

    private void SidebarLink(Paper p, string id, string text, Action action, IOrigamiIcon? icon = null) => NavigationItem(p, id, text, icon ?? LauncherIcons.GitHub, false, action);
    private void Nav(Paper p, string id, string name, int tab) => NavigationItem(
        p,
        id,
        name,
        tab switch
        {
            0 => OrigamiIconSet.Folder,
            1 => OrigamiIconSet.Layers,
            3 => LauncherIcons.Play,
            _ => LauncherIcons.Settings
        },
        _tab == tab,
        () => _tab = tab,
        tab == 1 ? NewerEditorRelease()?.Tag : null
    );
    private void NavigationItem(Paper p, string id, string text, IOrigamiIcon icon, bool selected, Action click, string? updateVersion = null)
    {
        Color color = selected ? _appearance.Accent : Ink;
        ElementBuilder button = p.Box(id)
            .Height(44)
            .Rounded(_appearance.Theme.Metrics.Rounding)
            .Cursor(PaperCursor.Pointer)
            .Tooltip(updateVersion == null ? Loc.Get(text) : Loc.Get(text) + "\n"
                                                                           + Loc.Get("launcher.versions.update_available", new
                                                                           {
                                                                               version = updateVersion
                                                                           }))
            .BackgroundColor(selected ? _appearance.Theme.Selected : Color.Transparent)
            .OnClick(_ => click())
            .Icon(p, icon, color, size: 24);
        button.Hovered.BackgroundColor(selected ? _appearance.Theme.Selected : _appearance.Theme.Hover);
        if (updateVersion != null)
        {
            using (button.Enter())
            {
                p.Box(id + "-update")
                    .PositionType(PositionType.SelfDirected)
                    .Left(SidebarWidth - 2 * SidebarPadding - 15).Top(5).Size(10, 10).Rounded(5)
                    .BackgroundColor(_appearance.Accent)
                    .BorderColor(_appearance.Panel).BorderWidth(2)
                    .IsNotInteractable();
            }
        }
    }

    private void Label(Paper p, string id, string text, float size, Color color, float height, bool bold = false)
    {
        float fontSize = Math.Max(16, size * 1.1f);
        p.Box(id)
            .Height(Math.Max(height, fontSize + 8))
            .IsNotInteractable()
            .Text(Loc.Get(text), bold ? _bold : _font)
            .FontSize(fontSize)
            .TextQuality(size >= 23 ? Scribe.FontQuality.Ultra : Scribe.FontQuality.Normal)
            .TextColor(color)
            .Alignment(TextAlignment.MiddleLeft);
    }

    private void Button(
        Paper p,
        string id,
        string text,
        Func<CancellationToken, Task> action,
        bool primary = false,
        float width = 100
    ) => Origami.Button(p, id, Loc.Get(text))
        .Width(width * 1.15f + 16)
        .Height(40)
        .Rounding(_appearance.Theme.Metrics.Rounding)
        .LeadingIcon(LauncherIcons.ForAction(text))
        .Variant(primary ? OrigamiVariant.Primary : OrigamiVariant.Default)
        .Disabled(Busy)
        .OnClick(() => Start(action, text))
        .Show();
    private IDisposable Card(Paper p, string id) => p.Column(id)
        .Height(UnitValue.Auto)
        .Padding(18)
        .Gap(8)
        .BackgroundColor(_appearance.Card)
        .BorderColor(_appearance.Theme.BorderSoft)
        .BorderWidth(1)
        .Rounded(_appearance.Theme.Metrics.ContainerRounding)
        .Enter();
    private IDisposable VersionCard(Paper p, string id) => p.Column(id)
        .Height(UnitValue.Auto)
        .Padding(14)
        .Gap(6)
        .BackgroundColor(_appearance.Card)
        .BorderColor(_appearance.Theme.BorderSoft)
        .BorderWidth(1)
        .Rounded(_appearance.Theme.Metrics.ContainerRounding)
        .Enter();
}
