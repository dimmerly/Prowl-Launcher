using Prowl.Launcher.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;

using Color=System.Drawing.Color;

namespace Prowl.Launcher;

sealed class Sidebar(LauncherStore store, Controls ui, Navigation navigation, EditorCoordinator editors)
{
    private LauncherAppearance Appearance => ui.Appearance;

    public void Draw(Paper p)
    {
        using (p.Column("sidebar")
            .Width(Constants.Layout.SidebarWidth)
            .Padding(Constants.Layout.SidebarPadding)
            .BackgroundLinearGradient(0, 0, 0, 1,
                OrigamiTheme.WithAlpha(Appearance.Panel, 230),
                OrigamiTheme.WithAlpha(Appearance.Panel, 220))
            .BackdropBlur(Appearance.Data.GlassBlur ? Math.Min(Appearance.Data.BlurAmount, 10) : 0)
            .Gap(8)
            .Enter())
        {
            using (p.Row("brand")
                .Height(60)
                .JustifyContent(LayoutJustification.Center)
                .AlignItems(LayoutAlignment.Center)
                .Enter())
            {
                Color logoColor = store.Settings.LogoColor is {} hex ? ColorRamp.ParseHex(hex) : ui.Ink;
                p.Box("brand-icon")
                    .Size(56, 56)
                    .IsNotInteractable()
                    .Icon(p, LauncherIcons.ProwlLogo, logoColor, size: 56);
            }

            p.Box("brand-divider")
                .Height(1)
                .BackgroundColor(Appearance.Theme.BorderSoft)
                .IsNotInteractable();

            Nav(p, "projects-nav", "launcher.projects.title", LauncherTab.Projects);
            Nav(p, "versions-nav", "launcher.versions.title", LauncherTab.Versions);
            Nav(p, "samples-nav", "launcher.samples.title", LauncherTab.Samples);
            p.Box("sidebar-spacer");
            Link(p, "github", "GitHub", () => ProcessHelper.OpenInShell(Constants.Network.GitHubUrl + store.Settings.ProwlRepository));
            Link(p, "discord", "Discord", () => ProcessHelper.OpenInShell("https://discord.gg/BqnJ9Rn4sn"), LauncherIcons.Discord);
            Nav(p, "settings-nav", "launcher.settings.title", LauncherTab.Settings);
        }
    }

    private void Link(Paper p, string id, string text, Action action, IOrigamiIcon? icon = null) => Item(p, id, text, icon ?? LauncherIcons.GitHub, false, action);

    private void Nav(Paper p, string id, string name, LauncherTab tab) => Item(
        p,
        id,
        name,
        tab switch
        {
            LauncherTab.Projects => OrigamiIconSet.Folder,
            LauncherTab.Versions => OrigamiIconSet.Layers,
            LauncherTab.Samples => LauncherIcons.Play,
            _ => LauncherIcons.Settings
        },
        navigation.Tab == tab,
        () => navigation.Tab = tab,
        tab == LauncherTab.Versions ? editors.NewerRelease()?.Tag : null
    );

    private void Item(Paper p, string id, string text, IOrigamiIcon icon, bool selected, Action click, string? updateVersion = null)
    {
        Color color = selected ? Appearance.Accent : ui.Ink;
        ElementBuilder button = p.Box(id)
            .Height(44)
            .Rounded(Appearance.Theme.Metrics.Rounding)
            .Cursor(PaperCursor.Pointer)
            .Tooltip(updateVersion == null ? Loc.Get(text) : Loc.Get(text) + "\n"
                                                                           + Loc.Get("launcher.versions.update_available", new
                                                                           {
                                                                               version = updateVersion
                                                                           }))
            .BackgroundColor(selected ? Appearance.Theme.Selected : Color.Transparent)
            .OnClick(_ => click())
            .Icon(p, icon, color, size: 24);
        button.Hovered.BackgroundColor(selected ? Appearance.Theme.Selected : Appearance.Theme.Hover);
        if (updateVersion != null)
        {
            using (button.Enter())
            {
                p.Box(id + "-update")
                    .PositionType(PositionType.SelfDirected)
                    .Left(Constants.Layout.SidebarWidth - 2 * Constants.Layout.SidebarPadding - 15).Top(5).Size(10, 10).Rounded(5)
                    .BackgroundColor(Appearance.Accent)
                    .BorderColor(Appearance.Panel).BorderWidth(2)
                    .IsNotInteractable();
            }
        }
    }
}
