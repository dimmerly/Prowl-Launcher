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
            if (_tab == 0)
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
            else
            {
                Origami.Dropdown(p, "channel", _channel, value => _channel = value, new[]
                    {
                        "launcher.versions.stable_and_preview",
                        "launcher.versions.stable_only"
                    })
                    .Display(value => Loc.Get(value == 0 ? "launcher.versions.stable_and_preview" : "launcher.versions.stable_only"))
                    .Width(210)
                    .IsItemEnabled(_ => !Busy)
                    .Show();
                Button(p, "refresh", "launcher.versions.refresh", token => RefreshAsync(token), width: 96);
                Button(p, "folder", "launcher.versions.install_folder", OpenInstallFolderAsync, width: 130);
            }
        }
    }
}
