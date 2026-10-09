using Prowl.Rosetta;
using Prowl.PaperUI;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private void DrawHeading(Paper p)
    {
        string title = _tab switch
        {
            0 => _newProjectPage ? Loc.Get("launcher.projects.new_title") : "launcher.projects.title",
            1 => "launcher.versions.title",
            3 => "launcher.samples.title",
            _ => "launcher.preferences.title"
        };
        string subtitle = _tab switch
        {
            0 => _newProjectPage ? Loc.Get("launcher.projects.choose_template") : "launcher.projects.subtitle",
            1 => "launcher.versions.subtitle",
            3 => "launcher.samples.subtitle",
            _ => "launcher.preferences.subtitle"
        };

        using (p.Column("heading").Height(76).Gap(6).Enter())
        {
            Label(p, "title", title, 28, Ink, 36, true);
            Label(p, "subtitle", subtitle, 14, Muted, 23);
        }
    }
}
