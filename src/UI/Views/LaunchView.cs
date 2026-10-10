using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private readonly List<EditorConsoleSession> _editorSessions = [];
    private EditorConsoleSession? _selectedEditorSession;

    private void DrawLaunchView(Paper p, float width, float height)
    {
        using (p.Column("window").Size(width, height).Enter())
        {
            using (p.Column("console-fullscreen").Size(width, height)
                .Padding(28, 28, 48, 16).Gap(12).BackgroundColor(_appearance.Background).Enter())
            {
                using (p.Row("console-heading").Height(36).Enter())
                {
                    Origami.Button(p, "console-back", Loc.Get("launcher.console.back"))
                        .Width(90).Height(32).Variant(OrigamiVariant.Subtle).OnClick(() => _tab = 0).Show();
                }
                float statusHeight = Math.Max(0, height - (_selectedEditorSession != null ? 156 : 112));
                DrawLaunchStatus(p, Math.Max(0, width - 56), statusHeight);
                if (_selectedEditorSession != null)
                {
                    using (p.Row("console-log-toggle-row").Height(32).JustifyContent(LayoutJustification.Center).Enter())
                    {
                        Origami.Button(p, "console-log-toggle", Loc.Get("launcher.console.show_logs"))
                            .Width(140).Height(32).Variant(OrigamiVariant.Subtle)
                            .OnClick(ShowLogsDialog).Show();
                    }
                }
            }
            DrawTitleBar(p);
        }
    }

    private void DrawLaunchStatus(Paper p, float width, float height)
    {
        EditorConsoleSession? session = _selectedEditorSession;
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
                CenteredLabel("console-status", Loc.Get(status), 24, Muted, 30);
                if (session != null)
                {
                    CenteredLabel("console-project", session.Request.Name, 44, Ink, 54, true);
                    CenteredLabel("console-version", session.Request.Version, 14, Muted, 20);
                }
            }
            if (session != null)
            {
                if (loading)
                {
                    using (p.Column("launch-output").Width(Math.Min(680, width)).Height(224)
                        .Padding(18).Gap(5)
                        .BorderColor(_appearance.Theme.BorderSoft).BorderWidth(1)
                        .Rounded(_appearance.Theme.Metrics.Rounding).Clip().Enter())
                    {
                        if (session.Lines.Count == 0)
                            p.Box("launch-output-empty").Height(26).IsNotInteractable()
                                .Text(Loc.Get("launcher.console.no_output"), _font).FontSize(18)
                                .TextColor(Muted);
                        for (int i = Math.Max(0, session.Lines.Count - 6); i < session.Lines.Count; i++)
                        {
                            EditorConsoleLine line = session.Lines[i];
                            p.Box("launch-output-" + i).Height(26).Clip()
                                .Text(line.Text, _font).FontSize(18)
                                .TextColor(line.IsError ? System.Drawing.Color.FromArgb(255, 135, 135)
                                    : line.IsWarning ? System.Drawing.Color.FromArgb(255, 209, 102)
                                    : Ink)
                                .Tooltip(line.Text);
                        }
                    }
                }
                CenteredLabel("console-elapsed", Loc.Get("launcher.console.elapsed", new { seconds = $"{session.Elapsed:F1}" }), 14, Muted, 24);
                if (session.State.Ended != null)
                    CenteredLabel("console-exit", session.State.Error ?? Loc.Get("launcher.console.exit_code", new { code = session.State.ExitCode }),
                        14, Muted, 26);
            }
            else CenteredLabel("console-empty-description", Loc.Get("launcher.console.empty_description"), 14, Muted, 30);
        }

        void CenteredLabel(string id, string text, float size, System.Drawing.Color color, float rowHeight, bool bold = false)
            => p.Box(id).Height(rowHeight).IsNotInteractable().Text(text, bold ? _bold : _font).FontSize(size)
                .TextColor(color).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter);
    }

    private void RefreshEditorConsoles()
    {
        foreach (EditorConsoleSession session in _editorSessions)
        {
            session.Refresh();
            if (session.State.Ready == null || session.ReadyHandled || Busy) continue;
            session.ReadyHandled = true;
            if (session.CloseWhenReady && session.Active)
            {
                session.CloseWhenReady = false;
                ForceClose();
                return;
            }
            if (_tab == 4 && _selectedEditorSession == session)
            {
                if (Modal.IsOpen) Modal.Pop();
                _tab = 0;
            }
        }
    }
}
