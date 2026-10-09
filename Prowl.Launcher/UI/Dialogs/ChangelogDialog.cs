using Prowl.Rosetta;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.Markdown;

using OpenTK.Mathematics;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private void ShowChangelog(EditorRelease release)
    {
        // Keep the builder alive for this dialog so the document is parsed once.
        MarkdownBuilder markdown = new MarkdownBuilder(_font).Fonts(bold: _bold)
            .FontSize(18)
            .Source(string.IsNullOrWhiteSpace(release.Notes) ? Loc.Get("launcher.versions.no_release_notes") : release.Notes)
            .OnLink(href =>
            {
                if (Uri.TryCreate(new Uri(release.PageUrl), href, out Uri? uri) && uri.Scheme is "https" or "http")
                {
                    Open(uri.AbsoluteUri);
                }
            });
        Vector2i framebuffer = _window.FramebufferSize;
        float scale = Math.Max(0.01f, (DisplayScale / UiScale)) * UiScale;
        float width = Math.Min(840, framebuffer.X / scale - 48);
        float height = Math.Max(100, Math.Min(480, framebuffer.Y / scale * 0.72f - 150));
        Origami.Modal(Loc.Get("launcher.versions.changelog") + ": " + release.Tag)
            .Width(width)
            .Content(p => Origami.ScrollView(p, "release-notes-scroll", width - 24, height)
                .SmoothScroll(true)
                .WheelStep(72)
                .Body(() =>
                {
                    markdown.Colors(Ink, Muted, _appearance.Accent, _appearance.Panel, _appearance.Theme.BorderSoft).Build(p, "release-notes-markdown");
                }))
            .Button(Loc.Get("launcher.versions.open_on_github"), () => Open(release.PageUrl))
            .Button(Loc.Get("launcher.common.close"), Modal.Pop, OrigamiVariant.Primary)
            .Show();
    }
}
