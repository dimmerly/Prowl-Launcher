using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

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
                Color logoColor = store.Settings.LogoColor is {} hex ? Theming.ColorRamp.ParseHex(hex) : Ink;
                p.Box("brand-icon")
                    .Size(56, 56)
                    .IsNotInteractable()
                    .Icon(p, LauncherIcons.ProwlLogo, logoColor, size: 56);
            }

            p.Box("brand-divider")
                .Height(1)
                .BackgroundColor(_appearance.Theme.BorderSoft)
                .IsNotInteractable();

            Nav(p, "projects-nav", "launcher.projects.title", 0);
            Nav(p, "versions-nav", "launcher.versions.title", 1);
            Nav(p, "samples-nav", "launcher.samples.title", 3);
            p.Box("sidebar-spacer");
            SidebarLink(p, "github", "GitHub", () => Open("https://github.com/" + store.Settings.ProwlRepository));
            SidebarLink(p, "discord", "Discord", () => Open("https://discord.gg/BqnJ9Rn4sn"), LauncherIcons.Discord);
            Nav(p, "settings-nav", "launcher.settings.title", 2);
        }
    }
}
