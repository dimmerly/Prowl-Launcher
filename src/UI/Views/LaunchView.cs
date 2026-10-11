using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>The full-screen view shown while an editor starts.</summary>
sealed class LaunchView(Controls ui, Navigation navigation, EditorController editors, LogsDialog logs, TitleBar titleBar)
{
    public void Draw(Paper p, float width, float height)
    {
        using (p.Column("window").Size(width, height).Enter())
        {
            using (p.Column("console-fullscreen").Size(width, height)
                .Padding(28, 28, 48, 16).Gap(12).BackgroundColor(ui.Appearance.Background).Enter())
            {
                using (p.Row("console-heading").Height(36).Enter())
                {
                    Origami.Button(p, "console-back", Loc.Get("launcher.console.back"))
                        .Width(90).Height(32).Variant(OrigamiVariant.Subtle).OnClick(() => navigation.Tab = LauncherTab.Projects).Show();
                }
                float statusHeight = Math.Max(0, height - (editors.SelectedSession != null ? 156 : 112));
                DrawLaunchStatus(p, Math.Max(0, width - 56), statusHeight);
                if (editors.SelectedSession != null)
                {
                    using (p.Row("console-log-toggle-row").Height(32).JustifyContent(LayoutJustification.Center).Enter())
                    {
                        Origami.Button(p, "console-log-toggle", Loc.Get("launcher.console.show_logs"))
                            .Width(140).Height(32).Variant(OrigamiVariant.Subtle)
                            .OnClick(logs.Show).Show();
                    }
                }
            }
            titleBar.Draw(p);
        }
    }

    private void DrawLaunchStatus(Paper p, float width, float height)
    {
        EditorConsoleSession? session = editors.SelectedSession;
        bool loading = session is { Active: true, State.Ready: null };
        string status = session?.State switch
        {
            null => "launcher.console.empty",
            { Error: not null } => "launcher.console.failed",
            { Ended: not null, ExitCode: 0 } => "launcher.console.exited",
            { Ended: not null } => "launcher.console.failed",
            { Ready: not null } => "launcher.console.ready",
            _ => "launcher.console.opening"
        };
        using (p.Column("editor-loading").Size(width, height)
            .AlignItems(LayoutAlignment.Center).JustifyContent(LayoutJustification.Center).Enter())
        using (p.Column("editor-loading-summary").Width(Math.Min(680, width)).Height(UnitValue.Auto)
            .Gap(18).AlignItems(LayoutAlignment.Center).Enter())
        {
            using (p.Column("launch-heading").Width(UnitValue.Percentage(100)).Height(UnitValue.Auto)
                .Gap(4).AlignItems(LayoutAlignment.Center).Enter())
            {
                CenteredLabel("console-status", Loc.Get(status), 24, ui.Muted, 30);
                if (session != null)
                {
                    CenteredLabel("console-project", session.Request.Name, 44, ui.Ink, 54, true);
                    CenteredLabel("console-version", session.Request.Version, 14, ui.Muted, 20);
                }
            }
            if (session != null)
            {
                if (loading)
                {
                    using (p.Column("launch-output").Width(Math.Min(680, width)).Height(224)
                        .Padding(18).Gap(5)
                        .BorderColor(ui.Appearance.Theme.BorderSoft).BorderWidth(1)
                        .Rounded(ui.Appearance.Theme.Metrics.Rounding).Clip().Enter())
                    {
                        if (session.Lines.Count == 0)
                        {
                            p.Box("launch-output-empty").Height(26).IsNotInteractable()
                                .Text(Loc.Get("launcher.console.no_output"), ui.Font).FontSize(18)
                                .TextColor(ui.Muted);
                        }
                        for (int i = Math.Max(0, session.Lines.Count - 6); i < session.Lines.Count; i++)
                        {
                            EditorConsoleLine line = session.Lines[i];
                            p.Box("launch-output-" + i).Height(26).Clip()
                                .Text(line.Text, ui.Font).FontSize(18)
                                .TextColor(line.IsError ? Color.FromArgb(255, 135, 135)
                                    : line.IsWarning ? Color.FromArgb(255, 209, 102)
                                    : ui.Ink)
                                .Tooltip(line.Text);
                        }
                    }
                }
                CenteredLabel("console-elapsed", Loc.Get("launcher.console.elapsed", new
                {
                    seconds = $"{session.Elapsed:F1}"
                }), 14, ui.Muted, 24);
                if (session.State.Ended != null)
                {
                    CenteredLabel("console-exit", session.State.Error ?? Loc.Get("launcher.console.exit_code", new
                        {
                            code = session.State.ExitCode
                        }),
                        14, ui.Muted, 26);
                }
            }
            else
            {
                CenteredLabel("console-empty-description", Loc.Get("launcher.console.empty_description"), 14, ui.Muted, 30);
            }
        }

        void CenteredLabel(string id, string text, float size, Color color, float rowHeight, bool bold = false)
            => p.Box(id).Height(rowHeight).IsNotInteractable().Text(text, bold ? ui.Bold : ui.Font).FontSize(size)
                .TextColor(color).Alignment(TextAlignment.MiddleCenter);
    }
}
