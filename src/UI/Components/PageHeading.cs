using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;
namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private void DrawHeading(Paper p, Action<Paper>? actions = null)
    {
        string title = _tab switch
        {
            LauncherTab.Projects => _newProjectPage ? Loc.Get("launcher.projects.new_title") : "launcher.projects.title",
            LauncherTab.Versions => "launcher.versions.title",
            LauncherTab.Samples => "launcher.samples.title",
            _ => "launcher.settings.title"
        };
        string subtitle = _tab switch
        {
            LauncherTab.Projects => _newProjectPage ? Loc.Get("launcher.projects.choose_template") : "launcher.projects.subtitle",
            LauncherTab.Versions => "launcher.versions.subtitle",
            LauncherTab.Samples => "launcher.samples.subtitle",
            _ => "launcher.settings.subtitle"
        };

        using (p.Row("heading").Height(76).Gap(16).AlignItems(LayoutAlignment.Center).Enter())
        {
            using (p.Column("heading-text").Height(76).Enter())
            {
                Label(p, "title", title, 32, Ink, 36, true, FontQuality.Ultra);
                Label(p, "subtitle", subtitle, 16, Muted, 23, quality: FontQuality.Ultra);
            }

            if (actions != null)
            {
                using (p.Row("heading-actions").Width(UnitValue.Auto).Height(UnitValue.Auto)
                    .Gap(8).AlignItems(LayoutAlignment.Center).Enter())
                {
                    actions(p);
                }
            }
        }
    }
}
