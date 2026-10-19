using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Launcher;

/// <summary>The bottom progress panel for the current <see cref="OperationCoordinator" /> operation.</summary>
internal sealed class OperationPanel(Controls ui)
{
    private OperationCoordinator Operations => ui.Operations;

    public void Draw(Paper p)
    {
        const float trackThickness = 10;
        using (p.Column("operation-panel")
                   .PositionType(PositionType.SelfDirected)
                   .Left(Constants.Layout.SidebarWidth)
                   .Top(p.ScreenRect.Size.Y - Constants.Layout.OperationHeight)
                   .Width(Math.Max(1, p.ScreenRect.Size.X - Constants.Layout.SidebarWidth))
                   .Height(Constants.Layout.OperationHeight)
                   .Gap(0)
                   .BackgroundColor(ui.Appearance.Panel)
                   .Enter())
        {
            p.Box("operation-divider")
                .Height(1)
                .BackgroundColor(ui.Appearance.Theme.BorderSoft)
                .IsNotInteractable();
            DrawHeader(p);

            using (p.Box("operation-track").Height(trackThickness).Clip().IsNotInteractable().Enter())
            {
                // Origami centers its track in a header-height row; crop that row to the track.
                float progressHeight = Math.Max(trackThickness, ui.Appearance.Theme.Metrics.HeaderHeight);
                using (p.Column("operation-track-content")
                           .PositionType(PositionType.SelfDirected)
                           .Top((trackThickness - progressHeight) / 2)
                           .Width(UnitValue.Percentage(100))
                           .Height(progressHeight)
                           .Enter())
                {
                    Origami.ProgressBar(p, "operation-progress", (float)(Operations.Fraction ?? 0))
                        .Indeterminate(Operations.Fraction == null)
                        .Thickness(trackThickness)
                        .Square()
                        .FillColor(ui.Appearance.Accent)
                        .Show();
                }
            }
        }
    }

    private void DrawHeader(Paper p)
    {
        bool cancelling = Operations.Cancelling;
        using (p.Row("operation-header")
                   .Height(Constants.Layout.OperationHeight - 11)
                   .Padding(28, 28, 4, 0)
                   .Gap(12)
                   .AlignItems(LayoutAlignment.Center)
                   .Enter())
        {
            using (p.Column("operation-info").Height(UnitValue.Auto).AlignSelf(LayoutAlignment.Start).Gap(0).Clip()
                       .Enter())
            {
                ui.Label(p, "operation-title", cancelling ? "launcher.operations.cancelling" : Operations.Title, 19,
                    ui.Ink, 28, true);
                ui.Label(p, "operation-detail", Operations.Detail, 14, ui.Muted, 22);
            }

            p.Box("operation-percent")
                .Width(100)
                .Height(28)
                .IsNotInteractable()
                .Text(
                    Operations.Fraction is double fraction
                        ? $"{Math.Clamp(fraction, 0, 1):P0}"
                        : Loc.Get("launcher.operations.working"), ui.Bold)
                .FontSize(18)
                .TextColor(ui.Appearance.Accent)
                .Alignment(TextAlignment.MiddleRight);
            Origami.Button(p, "cancel", Loc.Get("launcher.common.cancel"))
                .Width(108)
                .Height(40)
                .Disabled(cancelling)
                .OnClick(Operations.Cancel)
                .Show();
        }
    }
}
