using Prowl.Rosetta;
using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private void DrawToolbar(Paper p, float width)
    {
        using (p.Row("toolbar").Height(44).Gap(10).Enter())
        {
            Button(p, "add", "launcher.projects.add", AddProjectAsync, true, 125);
            Button(p, "new", "launcher.projects.new", ToggleProjectPageAsync, width: 138);
            p.Box("toolbar-spacer");

            float searchWidth = Math.Clamp(width - SidebarWidth - 56 - 380, 140, 240);
            Origami.TextField(p, "search", _search, value => _search = value)
                .Width(searchWidth)
                .Placeholder(Loc.Get("launcher.projects.search"))
                .Show();
        }
    }
}
