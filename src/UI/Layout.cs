using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private float _newsFooterHeight = 101;
    private LauncherTab _tab = initialTab;

    private void OnGui(Paper p)
    {
        float width = p.ScreenRect.Size.X;
        float height = p.ScreenRect.Size.Y;
        bool progressVisible = ShowProgress;
        using IDisposable theme = Origami.PushTheme(_appearance.Theme);

        ShowPendingNotifications();
        Origami.BeginFrame(p, _deltaTime);
        if (_showInstallationPrompt)
        {
            _showInstallationPrompt = false;
            ShowInstallationDialog();
        }

        if (_tab == LauncherTab.Launch)
        {
            DrawLaunchView(p, width, height);
            DrawNotifications(p, false);
            Origami.EndFrame(p);
            return;
        }

        using (p.Column("window").Size(width, height).Enter())
        {
            using (p.Row("launcher")
                .Size(width, height)
                .BackgroundColor(_appearance.Background)
                .Enter())
            {
                p.Box("sidebar-backdrop")
                    .PositionType(PositionType.SelfDirected)
                    .Left(0)
                    .Top(0)
                    .Size(Constants.Layout.SidebarWidth, height)
                    .BackgroundLinearGradient(0, 0, 1, 1,
                        OrigamiTheme.WithAlpha(_appearance.Accent, 14),
                        _appearance.Background)
                    .IsNotInteractable();
                DrawSidebar(p);

                using (p.Column("content")
                    .Padding(28, 28, 24, 18)
                    .Gap(14)
                    .Enter())
                {
                    DrawHeading(p, _tab == LauncherTab.Samples ? DrawSamplesHeadingActions : null);

                    bool showToolbar = _tab == LauncherTab.Projects && !_newProjectPage;
                    if (showToolbar)
                    {
                        DrawToolbar(p);
                    }

                    // Keep the list and news above the fixed bottom progress bar.
                    float operationSpace = progressVisible ? Constants.Layout.OperationHeight : 0;
                    float toolbarSpace = showToolbar ? 0 : 58;
                    float listWidth = Math.Max(1, ContentWidth(p));
                    float listHeight = Math.Max(0, height - (showToolbar ? 190 : 202) - operationSpace + toolbarSpace);
                    // Sample cards scale up on hover; leave room so the scroll clip doesn't cut them.
                    float samplesInset = _tab == LauncherTab.Samples ? 8 : 0;
                    float scrollHeight = showToolbar ? Math.Max(0, listHeight - _newsFooterHeight - 14) : listHeight;

                    Origami.ScrollView(p, "list-" + (int)_tab, listWidth, scrollHeight)
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
                            DrawNewsSection(p);
                        }
                    }
                }

                if (progressVisible)
                {
                    DrawOperation(p);
                }
            }
            DrawTitleBar(p);
        }

        DrawNotifications(p, progressVisible);
        Origami.EndFrame(p);
    }

    private void DrawSelectedPage(Paper p)
    {
        switch (_tab)
        {
            case LauncherTab.Projects:
                DrawProjects(p);
                break;
            case LauncherTab.Versions:
                DrawVersions(p);
                break;
            case LauncherTab.Samples:
                DrawSamples(p);
                break;
            default:
                DrawSettings(p);
                break;
        }
    }
}
