using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Scribe;

namespace Prowl.Launcher;

/// <summary>The window shell: sidebar, page heading, the selected page, news, progress panel and toasts.</summary>
public sealed partial class Launcher
{
    private float _newsFooterHeight = 101;

    /// <summary>The progress panel is hidden on the launch view and while a sample card shows its own spinner.</summary>
    private bool ShowProgress => _navigation.Tab != LauncherTab.Launch
                                 && !(Samples.Launching && !Operations.ImmediateProgress)
                                 && Operations.ProgressDue;

    private void OnGui(Paper p)
    {
        float width = p.ScreenRect.Size.X;
        float height = p.ScreenRect.Size.Y;
        bool progressVisible = ShowProgress;
        using IDisposable theme = Origami.PushTheme(Appearance.Theme);

        Notifications.ShowPending();
        Origami.BeginFrame(p, _deltaTime);
        _installation.ShowIfPending();
        _uninstall.ShowIfPending();

        if (_navigation.Tab == LauncherTab.Launch)
        {
            _launchView.Draw(p, width, height);
            Notifications.Draw(p, _deltaTime, false);
            Origami.EndFrame(p);
            return;
        }

        using (p.Column("window").Size(width, height).Enter())
        {
            using (p.Row("launcher")
                       .Size(width, height)
                       .BackgroundColor(Appearance.Background)
                       .Enter())
            {
                p.Box("sidebar-backdrop")
                    .PositionType(PositionType.SelfDirected)
                    .Left(0)
                    .Top(0)
                    .Size(Constants.Layout.SidebarWidth, height)
                    .BackgroundLinearGradient(0, 0, 1, 1,
                        OrigamiTheme.WithAlpha(Appearance.Accent, 14),
                        Appearance.Background)
                    .IsNotInteractable();
                _sidebar.Draw(p);

                using (p.Column("content")
                           .Padding(28, 28, 24, 18)
                           .Gap(14)
                           .Enter())
                {
                    DrawHeading(p, _navigation.Tab == LauncherTab.Samples ? Samples.DrawHeadingActions : null);

                    bool showToolbar = _navigation.Tab == LauncherTab.Projects && !_projects.ShowingNewProject;
                    if (showToolbar)
                    {
                        _projects.DrawToolbar(p);
                    }

                    // Keep the list and news above the fixed bottom progress bar.
                    float operationSpace = progressVisible ? Constants.Layout.OperationHeight : 0;
                    float toolbarSpace = showToolbar ? 0 : 58;
                    float listWidth = Math.Max(1, Controls.ContentWidth(p));
                    float listHeight = Math.Max(0, height - (showToolbar ? 190 : 202) - operationSpace + toolbarSpace);
                    // Sample cards scale up on hover; leave room so the scroll clip doesn't cut them.
                    float samplesInset = _navigation.Tab == LauncherTab.Samples ? 8 : 0;
                    float scrollHeight = showToolbar ? Math.Max(0, listHeight - _newsFooterHeight - 14) : listHeight;

                    Origami.ScrollView(p, "list-" + (int)_navigation.Tab, listWidth, scrollHeight)
                        .OverlayScrollbars(false)
                        .Padding(samplesInset, 8, samplesInset, samplesInset)
                        .SmoothScroll()
                        .WheelStep(72)
                        .ColSpacing(12)
                        .Body(() => DrawSelectedPage(p));
                    if (showToolbar)
                    {
                        using (p.Column("projects-footer").Height(UnitValue.Auto).Gap(12)
                                   .OnPostLayout((_, rect) => _newsFooterHeight = rect.Size.Y).Enter())
                        {
                            News.Draw(p);
                        }
                    }
                }

                if (progressVisible)
                {
                    _operationPanel.Draw(p);
                }
            }

            _titleBar.Draw(p);
        }

        Notifications.Draw(p, _deltaTime, progressVisible);
        Origami.EndFrame(p);
    }

    private void DrawSelectedPage(Paper p)
    {
        switch (_navigation.Tab)
        {
            case LauncherTab.Projects:
                _projects.Draw(p);
                break;
            case LauncherTab.Versions:
                _versions.Draw(p);
                break;
            case LauncherTab.Samples:
                Samples.Draw(p);
                break;
            default:
                _settings.Draw(p);
                break;
        }
    }

    private void DrawHeading(Paper p, Action<Paper>? actions = null)
    {
        bool newProject = _projects.ShowingNewProject;
        string title = _navigation.Tab switch
        {
            LauncherTab.Projects => newProject ? Loc.Get("launcher.projects.new_title") : "launcher.projects.title",
            LauncherTab.Versions => "launcher.versions.title",
            LauncherTab.Samples => "launcher.samples.title",
            _ => "launcher.settings.title"
        };
        string subtitle = _navigation.Tab switch
        {
            LauncherTab.Projects => newProject
                ? Loc.Get("launcher.projects.choose_template")
                : "launcher.projects.subtitle",
            LauncherTab.Versions => "launcher.versions.subtitle",
            LauncherTab.Samples => "launcher.samples.subtitle",
            _ => "launcher.settings.subtitle"
        };

        using (p.Row("heading").Height(76).Gap(16).AlignItems(LayoutAlignment.Center).Enter())
        {
            using (p.Column("heading-text").Height(76).Enter())
            {
                _ui.Label(p, "title", title, 32, _ui.Ink, 36, true, FontQuality.Ultra);
                _ui.Label(p, "subtitle", subtitle, 16, _ui.Muted, 23, quality: FontQuality.Ultra);
            }

            if (actions != null)
            {
                using (p.Row("heading-actions").Width(UnitValue.Auto).Height(UnitValue.Auto)
                           .Gap(8).AlignItems(LayoutAlignment.Center).Enter())
                {
                    actions(p);
                }
            }
        }
    }
}
