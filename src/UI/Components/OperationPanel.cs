using Prowl.Rosetta;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using TextAlignment=Prowl.PaperUI.TextAlignment;

namespace Prowl.Launcher;

public sealed partial class Launcher
{

    private void DrawOperationHeader(Paper p)
    {
        bool cancelling = _operation?.IsCancellationRequested == true;
        using (p.Row("operation-header")
            .Height(Constants.Layout.OperationHeight - 11)
            .Padding(28, 28, 4, 0)
            .Gap(12)
            .AlignItems(LayoutAlignment.Center)
            .Enter())
        {
            using (p.Column("operation-info").Height(UnitValue.Auto).AlignSelf(LayoutAlignment.Start).Gap(0).Clip().Enter())
            {
                Label(p, "operation-title", cancelling ? "launcher.operations.cancelling" : _operationTitle, 16, Ink, 28, true);
                Label(p, "operation-detail", _operationDetail, 12, Muted, 22);
            }
            p.Box("operation-percent")
                .Width(100)
                .Height(28)
                .IsNotInteractable()
                .Text(_fraction is double fraction ? $"{Math.Clamp(fraction, 0, 1):P0}" : Loc.Get("launcher.operations.working"), _bold)
                .FontSize(18)
                .TextColor(_appearance.Accent)
                .Alignment(TextAlignment.MiddleRight);
            Origami.Button(p, "cancel", Loc.Get("launcher.common.cancel"))
                .Width(108)
                .Height(40)
                .Disabled(cancelling)
                .OnClick(() => _operation?.Cancel())
                .Show();
        }
    }

    private void DrawOperation(Paper p)
    {
        const float trackThickness = 10;
        using (p.Column("operation-panel")
            .PositionType(PositionType.SelfDirected)
            .Left(Constants.Layout.SidebarWidth)
            .Top((float)p.ScreenRect.Size.Y - Constants.Layout.OperationHeight)
            .Width(Math.Max(1, (float)p.ScreenRect.Size.X - Constants.Layout.SidebarWidth))
            .Height(Constants.Layout.OperationHeight)
            .Gap(0)
            .BackgroundColor(_appearance.Panel)
            .Enter())
        {
            p.Box("operation-divider")
                .Height(1)
                .BackgroundColor(_appearance.Theme.BorderSoft)
                .IsNotInteractable();
            DrawOperationHeader(p);

            using (p.Box("operation-track").Height(trackThickness).Clip().IsNotInteractable().Enter())
            {
                // Origami centers its track in a header-height row; crop that row to the track.
                float progressHeight = Math.Max(trackThickness, _appearance.Theme.Metrics.HeaderHeight);
                using (p.Column("operation-track-content")
                    .PositionType(PositionType.SelfDirected)
                    .Top((trackThickness - progressHeight) / 2)
                    .Width(UnitValue.Percentage(100))
                    .Height(progressHeight)
                    .Enter())
                {
                    Origami.ProgressBar(p, "operation-progress", (float)(_fraction ?? 0))
                        .Indeterminate(_fraction == null)
                        .Thickness(trackThickness)
                        .Square()
                        .FillColor(_appearance.Accent)
                        .Show();
                }
            }
        }
    }
}
