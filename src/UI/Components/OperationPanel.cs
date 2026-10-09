using Prowl.Rosetta;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using TextAlignment=Prowl.PaperUI.TextAlignment;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private const float OperationHeight = 148;

    private void DrawOperation(Paper p)
    {
        bool cancelling = _operation?.IsCancellationRequested == true;
        using (p.Column("operation-panel")
            .Height(OperationHeight)
            .Padding(16)
            .Gap(8)
            .BackgroundColor(_appearance.Panel)
            .BorderColor(_appearance.Accent)
            .BorderWidth(1)
            .Rounded(_appearance.Theme.Metrics.ContainerRounding)
            .Enter())
        {
            using (p.Row("operation-header")
                .Height(44)
                .Gap(12)
                .Enter())
            {
                Label(p, "operation-title", cancelling ? "launcher.operations.cancelling" : _operationTitle, 16, Ink, 28, true);
                p.Box("operation-percent")
                    .Width(100)
                    .Height(44)
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

            Label(p, "operation-detail", _operationDetail, 12, Muted, 22);
            Origami.ProgressBar(p, "operation-progress", (float)(_fraction ?? 0))
                .Indeterminate(_fraction == null)
                .Thickness(12)
                .FillColor(_appearance.Accent)
                .Show();
        }
    }
}
