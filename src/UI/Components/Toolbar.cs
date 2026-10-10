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

            bool hasLogs = _editorSessions.Any(session => session.Lines.Count > 0);
            if (hasLogs)
                Origami.Button(p, "project-logs", Loc.Get("launcher.console.logs"))
                    .Width(76).Height(40).Variant(OrigamiVariant.Subtle).OnClick(ShowLogsDialog).Show();
            float searchWidth = Math.Clamp(width - Constants.Layout.SidebarWidth - 56 - 380 - (hasLogs ? 86 : 0), 140, 240);
            Origami.TextField(p, "search", _search, value => _search = value)
                .Width(searchWidth)
                .Placeholder(Loc.Get("launcher.projects.search"))
                .Show();
        }
    }
}
