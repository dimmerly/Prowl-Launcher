using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private int _tab = initialTab;

    private void OnGui(Paper p)
    {
        float width = (float)p.ScreenRect.Size.X;
        float height = (float)p.ScreenRect.Size.Y;
        bool progressVisible = ShowProgress;
        using IDisposable theme = Origami.PushTheme(_appearance.Theme);

        ShowPendingNotifications();
        Origami.BeginFrame(p, _deltaTime);
        if (_showInstallationPrompt)
        {
            _showInstallationPrompt = false;
            ShowInstallationDialog();
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
                    .Size(SidebarWidth, height)
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
                    DrawHeading(p);

                    if (progressVisible)
                    {
                        DrawOperation(p);
                    }

                    bool showToolbar = _tab == 0 && !_newProjectPage;
                    if (showToolbar)
                    {
                        DrawToolbar(p, width);
                    }

                    // Reserve the operation panel and toolbar before sizing the list.
                    float operationSpace = progressVisible ? OperationHeight + 14 : 0;
                    float toolbarSpace = showToolbar ? 0 : 58;
                    float listWidth = Math.Max(1, width - SidebarWidth - 56);
                    float listHeight = Math.Max(0, height - 202 - operationSpace + toolbarSpace);

                    Origami.ScrollView(p, "list-" + _tab, listWidth, listHeight)
                        .OverlayScrollbars(false)
                        .Padding(_tab == 3 ? 8 : 0, 8, _tab == 3 ? 8 : 0, _tab == 3 ? 8 : 0)
                        .SmoothScroll(true)
                        .WheelStep(72)
                        .ColSpacing(12)
                        .Body(() => DrawSelectedPage(p));
                }
            }
            DrawTitleBar(p);
        }

        Origami.EndFrame(p);
    }

    private void DrawSelectedPage(Paper p)
    {
        switch (_tab)
        {
            case 0:
                DrawProjects(p);
                break;
            case 1:
                DrawVersions(p);
                break;
            case 3:
                DrawSamples(p);
                break;
            default:
                DrawSettings(p);
                break;
        }
    }
}
