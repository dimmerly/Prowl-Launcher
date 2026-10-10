using Prowl.Rosetta;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private void DrawHeading(Paper p, Action<Paper>? actions = null)
    {
        string title = _tab switch
        {
            0 => _newProjectPage ? Loc.Get("launcher.projects.new_title") : "launcher.projects.title",
            1 => "launcher.versions.title",
            3 => "launcher.samples.title",
            _ => "launcher.settings.title"
        };
        string subtitle = _tab switch
        {
            0 => _newProjectPage ? Loc.Get("launcher.projects.choose_template") : "launcher.projects.subtitle",
            1 => "launcher.versions.subtitle",
            3 => "launcher.samples.subtitle",
            _ => "launcher.settings.subtitle"
        };

        using (p.Row("heading").Height(76).Gap(16).AlignItems(LayoutAlignment.Center).Enter())
        {
            using (p.Column("heading-text").Height(76).Enter())
            {
                Label(p, "title", title, 28, Ink, 36, true);
                Label(p, "subtitle", subtitle, 14, Muted, 23);
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
