using System.Drawing;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>Console output of the editors started from this launcher, with search and a problems filter.</summary>
sealed class LogsDialog(Controls ui, EditorCoordinator editors)
{
    private bool _logsProblemsOnly;
    private string _logsSearch = "";
    private EditorConsoleSession? _selectedLogSession;

    /// <summary>Clears the search and filter, as for a newly launched editor.</summary>
    public void Reset()
    {
        _logsSearch = "";
        _logsProblemsOnly = false;
    }

    public void Show()
    {
        if (editors.SelectedSession == null || Modal.IsOpen)
        {
            return;
        }
        _selectedLogSession = editors.SelectedSession;
        float width = Math.Clamp(ui.ScreenSize.X - 48, 25, 1000);
        float height = Math.Clamp(ui.ScreenSize.Y - 180, 1, 600);
        Origami.Modal(Loc.Get("launcher.console.title"))
            .Width(width)
            .CenteredContent(p => DrawLogs(p, width - 24, height))
            .Button(Loc.Get("launcher.common.close"), Modal.Pop)
            .Show();
    }

    private void DrawLogs(Paper p, float width, float height)
    {
        if (_selectedLogSession is not {} session)
        {
            return;
        }

        using (p.Column("editor-console-view").Size(width, height).Gap(12).Enter())
        {
            if (editors.Sessions.Count > 1)
            {
                Controls.AboveParentLayer(p, () => Origami.Dropdown(p, "console-session-picker", session.Id,
                        key => _selectedLogSession = editors.Sessions.Single(s => s.Id == key),
                        editors.Sessions.Select(s => s.Id).ToArray())
                    .Display(key =>
                    {
                        EditorConsoleSession item = editors.Sessions.Single(s => s.Id == key);
                        return $"{editors.Sessions.ToList().IndexOf(item) + 1}. {item.Request.Name} · {item.Request.Version} · {item.State.Started.ToLocalTime():HH:mm:ss}";
                    })
                    .Show());
            }
            using (p.Row("console-tools").Height(40).Gap(10).Enter())
            {
                Origami.TextField(p, "console-search", _logsSearch, value => _logsSearch = value)
                    .Placeholder(Loc.Get("launcher.console.search")).Show();
                Origami.Button(p, "console-problems", Loc.Get(_logsProblemsOnly ? "launcher.console.all_output" : "launcher.console.problems"))
                    .Width(170).OnClick(() => _logsProblemsOnly = !_logsProblemsOnly).Show();
                Origami.Button(p, "console-copy", Loc.Get("launcher.console.copy"))
                    .Width(115).OnClick(() => p.SetClipboard(string.Join(Environment.NewLine,
                        session.Lines.Select(line => $"[{line.Time.ToLocalTime():HH:mm:ss}] {line.Text}")))).Show();
                p.Box("logs-disclaimer").Width(20).Height(40)
                    .Text("\u24d8", ui.Font).FontSize(15).TextColor(ui.Muted)
                    .Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(Loc.Get("launcher.console.latest_first"));
            }
            Origami.ScrollView(p, "console-output-" + session.Id, width,
                    Math.Max(0, height - 52 - (editors.Sessions.Count > 1 ? 52 : 0)))
                .OverlayScrollbars(false).Padding(12).SmoothScroll().WheelStep(72).ColSpacing(2)
                .Body(() =>
                {
                    int shown = 0;
                    List<EditorConsoleLine> lines = _logsProblemsOnly ? session.Problems : session.Lines;
                    for (int i = lines.Count - 1; i >= 0; i--)
                    {
                        EditorConsoleLine line = lines[i];
                        if (!line.Text.Contains(_logsSearch, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        shown++;
                        p.Box("console-line-" + i).Height(26).Clip()
                            .Text($"{line.Time.ToLocalTime():HH:mm:ss}  {line.Text}", ui.Font).FontSize(15)
                            .TextColor(line.IsError ? Color.IndianRed : line.IsWarning ? Color.Goldenrod : ui.Ink)
                            .Tooltip(line.Text);
                    }
                    if (shown == 0)
                    {
                        ui.Label(p, "console-no-output", "launcher.console.no_output", 14, ui.Muted, 32);
                    }
                });
        }
    }
}
