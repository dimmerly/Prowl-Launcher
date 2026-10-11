using Prowl.Launcher.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;

using Color=System.Drawing.Color;

namespace Prowl.Launcher;

public sealed partial class Launcher
{

    private void DrawSidebar(Paper p)
    {
        using (p.Column("sidebar")
            .Width(Constants.Layout.SidebarWidth)
            .Padding(Constants.Layout.SidebarPadding)
            .BackgroundLinearGradient(0, 0, 0, 1,
                OrigamiTheme.WithAlpha(_appearance.Panel, 230),
                OrigamiTheme.WithAlpha(_appearance.Panel, 220))
            .BackdropBlur(_appearance.Data.GlassBlur ? Math.Min(_appearance.Data.BlurAmount, 10) : 0)
            .Gap(8)
            .Enter())
        {
            using (p.Row("brand")
                .Height(60)
                .JustifyContent(LayoutJustification.Center)
                .AlignItems(LayoutAlignment.Center)
                .Enter())
            {
                Color logoColor = store.Settings.LogoColor is {} hex ? ColorRamp.ParseHex(hex) : Ink;
                p.Box("brand-icon")
                    .Size(56, 56)
                    .IsNotInteractable()
                    .Icon(p, LauncherIcons.ProwlLogo, logoColor, size: 56);
            }

            p.Box("brand-divider")
                .Height(1)
                .BackgroundColor(_appearance.Theme.BorderSoft)
                .IsNotInteractable();

            Nav(p, "projects-nav", "launcher.projects.title", LauncherTab.Projects);
            Nav(p, "versions-nav", "launcher.versions.title", LauncherTab.Versions);
            Nav(p, "samples-nav", "launcher.samples.title", LauncherTab.Samples);
            p.Box("sidebar-spacer");
            SidebarLink(p, "github", "GitHub", () => ProcessHelper.OpenInShell(Constants.Network.GitHubUrl + store.Settings.ProwlRepository));
            SidebarLink(p, "discord", "Discord", () => ProcessHelper.OpenInShell("https://discord.gg/BqnJ9Rn4sn"), LauncherIcons.Discord);
            Nav(p, "settings-nav", "launcher.settings.title", LauncherTab.Settings);
        }
    }
}
