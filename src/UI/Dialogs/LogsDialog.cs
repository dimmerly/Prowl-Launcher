using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private EditorConsoleSession? _selectedLogSession;
    private string _logsSearch = "";
    private bool _logsProblemsOnly;

    private void ShowLogsDialog()
    {
        if (_selectedEditorSession == null || Modal.IsOpen)
        {
            return;
        }
        _selectedLogSession = _selectedEditorSession;
        float width = Math.Clamp(_window.FramebufferSize.X / DisplayScale - 48, 25, 1000);
        float height = Math.Clamp(_window.FramebufferSize.Y / DisplayScale - 180, 1, 600);
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
            if (_editorSessions.Count > 1)
            {
                int parentLayer = 0;
                for (ElementHandle parent = p.CurrentParent; parent.IsValid; parent = parent.GetParentHandle())
                {
                    parentLayer = Math.Max(parentLayer, parent.Data.Layer);
                }
                int firstElement = p.ElementCount;
                Origami.Dropdown(p, "console-session-picker", session.Id,
                        key => _selectedLogSession = _editorSessions.Single(s => s.Id == key),
                        _editorSessions.Select(s => s.Id).ToArray())
                    .Display(key =>
                    {
                        EditorConsoleSession item = _editorSessions.Single(s => s.Id == key);
                        return $"{_editorSessions.IndexOf(item) + 1}. {item.Request.Name} · {item.Request.Version} · {item.State.Started.ToLocalTime():HH:mm:ss}";
                    })
                    .Show();
                // Origami 3.6.7 uses absolute overlay layers for dropdowns. Rebase them
                // above the containing dialog so both the menu and its backdrop work.
                for (int i = firstElement; i < p.ElementCount; i++)
                {
                    ref ElementData element = ref p.GetElementData(i);
                    if (element.Layer > 0)
                    {
                        element.Layer += parentLayer;
                    }
                }
            }
            using (p.Row("console-tools").Height(40).Gap(10).Enter())
            {
                Origami.TextField(p, "console-search", _logsSearch, value => _logsSearch = value)
                    .Placeholder(Loc.Get("launcher.console.search")).Show();
                Origami.Button(p, "console-problems", Loc.Get(_logsProblemsOnly ? "launcher.console.all_output" : "launcher.console.problems"))
                    .Width(170).OnClick(() => _logsProblemsOnly = !_logsProblemsOnly).Show();
                Origami.Button(p, "console-copy", Loc.Get("launcher.console.copy"))
                    .Width(115).OnClick(() => _window.ClipboardString = string.Join(Environment.NewLine,
                        session.Lines.Select(line => $"[{line.Time.ToLocalTime():HH:mm:ss}] {line.Text}"))).Show();
                p.Box("logs-disclaimer").Width(20).Height(40)
                    .Text("\u24d8", _font).FontSize(15).TextColor(Muted)
                    .Alignment(TextAlignment.MiddleCenter)
                    .Tooltip(Loc.Get("launcher.console.latest_first"));
            }
            Origami.ScrollView(p, "console-output-" + session.Id, width,
                    Math.Max(0, height - 52 - (_editorSessions.Count > 1 ? 52 : 0)))
                .OverlayScrollbars(false).Padding(12).SmoothScroll(true).WheelStep(72).ColSpacing(2)
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
                            .Text($"{line.Time.ToLocalTime():HH:mm:ss}  {line.Text}", _font).FontSize(15)
                            .TextColor(line.IsError ? System.Drawing.Color.IndianRed : line.IsWarning ? System.Drawing.Color.Goldenrod : Ink)
                            .Tooltip(line.Text);
                    }
                    if (shown == 0)
                    {
                        Label(p, "console-no-output", "launcher.console.no_output", 14, Muted, 32);
                    }
                });
        }
    }
}
