using Prowl.Rosetta;
using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private void DrawChangelogButton(Paper p, string id, EditorRelease release, bool subtle = false) => Origami.Button(p, id, "")
        .LeadingIcon(OrigamiIconSet.File)
        .Width(subtle ? 28 : 40)
        .Height(subtle ? 28 : 40)
        .Rounding(_appearance.Theme.Metrics.Rounding)
        .Variant(subtle ? OrigamiVariant.Subtle : OrigamiVariant.Default)
        .Tooltip(Loc.Get("launcher.versions.changelog"))
        .Disabled(Busy)
        .OnClick(() => ShowChangelog(release))
        .Show();
}
