using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.PaperUI.Markdown;
using Prowl.Rosetta;
using Prowl.Scribe;
using Prowl.Vector;
namespace Prowl.Launcher;

/// <summary>Confirmations, release notes and the folder picker shared by several views.</summary>
sealed class Dialogs(Controls ui)
{
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        CancellationToken token,
        string? sdkUrl = null,
        EditorRelease? changelog = null)
    {
        TaskCompletionSource<bool> completion = new();
        Float2 screen = ui.ScreenSize;
        float width = Math.Min(changelog == null ? 560 : 840, screen.X - 48);
        float notesHeight = Math.Max(100, Math.Min(360, screen.Y * 0.72f - 200));
        MarkdownBuilder? markdown = changelog == null ? null : ReleaseNotesMarkdown(changelog);
        ModalBuilder dialog = Origami.Modal(Loc.Get(title))
            .Width(width)
            .CenteredContent(p =>
            {
                using (p.Column("confirmation-content").Height(UnitValue.Auto).Gap(16).Enter())
                {
                    p.Box("confirmation-message")
                        .Width(UnitValue.Stretch())
                        .Height(UnitValue.Auto)
                        .Text(Loc.Get(message), ui.Font)
                        .FontSize(18)
                        .TextColor(ui.Ink)
                        .Wrap(TextWrapMode.Wrap)
                        .IsNotInteractable();
                    if (markdown != null)
                    {
                        ui.Label(p, "update-changelog-title", "launcher.versions.changelog", 18, ui.Ink, 28, true);
                        Origami.ScrollView(p, "update-changelog-scroll", width - 24, notesHeight)
                            .SmoothScroll()
                            .WheelStep(72)
                            .Body(() => ui.StyleMarkdown(markdown).Build(p, "release-notes-markdown"));
                    }
                }
            })
            .Button(Loc.Get("launcher.common.cancel"), () => Complete(false));

        if (sdkUrl != null)
        {
            dialog.Button(Loc.Get("launcher.sdk.get"), () =>
            {
                Modal.Pop();
                ProcessHelper.OpenInShell(sdkUrl);
                completion.TrySetResult(false);
            });
        }

        // Waiting for a decision is not part of the operation's progress delay.
        using (ui.Operations.WaitForUser())
        {
            dialog.Button(Loc.Get(accept), () => Complete(true), OrigamiVariant.Primary)
                .OnDismissed(_ => completion.TrySetResult(false))
                .Show();

            return await completion.Task.WaitAsync(token);
        }

        void Complete(bool accepted)
        {
            Modal.Pop();
            completion.TrySetResult(accepted);
        }
    }

    /// <summary>Shows a folder picker; waiting on the user doesn't count towards the progress delay.</summary>
    public async Task<string?> PickFolderAsync(string initialDirectory, CancellationToken token)
    {
        TaskCompletionSource<string?> completion = new();
        using (ui.Operations.WaitForUser())
        {
            Directory.CreateDirectory(initialDirectory);
            Origami.OpenFileDialog(FileDialogMode.SelectFolder, selected => completion.TrySetResult(selected), initialDirectory);
            return await completion.Task.WaitAsync(token);
        }
    }

    public void ShowChangelog(EditorRelease release)
    {
        // Keep the builder alive for this dialog so the document is parsed once.
        MarkdownBuilder markdown = ReleaseNotesMarkdown(release);
        Float2 screen = ui.ScreenSize;
        float width = Math.Min(840, screen.X - 48);
        float height = Math.Max(100, Math.Min(480, screen.Y * 0.72f - 150));
        Origami.Modal(Loc.Get("launcher.versions.changelog") + ": " + release.Tag)
            .Width(width)
            .CenteredContent(p => Origami.ScrollView(p, "release-notes-scroll", width - 24, height)
                .SmoothScroll()
                .WheelStep(72)
                .Body(() => ui.StyleMarkdown(markdown).Build(p, "release-notes-markdown")))
            .Button(Loc.Get("launcher.versions.open_on_github"), () => ProcessHelper.OpenInShell(release.PageUrl))
            .Button(Loc.Get("launcher.common.close"), Modal.Pop, OrigamiVariant.Primary)
            .Show();
    }

    public void ChangelogButton(Paper p, string id, EditorRelease release, bool subtle = false) => Origami.Button(p, id, "")
        .LeadingIcon(OrigamiIconSet.File)
        .Width(subtle ? 28 : 40)
        .Height(subtle ? 28 : 40)
        .Rounding(ui.Appearance.Theme.Metrics.Rounding)
        .Variant(subtle ? OrigamiVariant.Subtle : OrigamiVariant.Default)
        .Tooltip(Loc.Get("launcher.versions.changelog"))
        .Disabled(ui.Busy)
        .OnClick(() => ShowChangelog(release))
        .Show();

    private MarkdownBuilder ReleaseNotesMarkdown(EditorRelease release) => new MarkdownBuilder(ui.Font).Fonts(bold: ui.Bold)
        .FontSize(18)
        .Source(string.IsNullOrWhiteSpace(release.Notes) ? Loc.Get("launcher.versions.no_release_notes") : release.Notes)
        .OnLink(href =>
        {
            if (Uri.TryCreate(release.PageUrl, UriKind.Absolute, out Uri? page)
                && Uri.TryCreate(page, href, out Uri? uri) && uri.Scheme is "https" or "http")
            {
                ProcessHelper.OpenInShell(uri.AbsoluteUri);
            }
        });
}
