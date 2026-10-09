using Prowl.Rosetta;

using System.Diagnostics;

using Prowl.OrigamiUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private async Task<bool> ConfirmAsync(
        string title,
        string message,
        string accept,
        CancellationToken token,
        string? sdkUrl = null)
    {
        TaskCompletionSource<bool> completion = new();
        float scale = Math.Max(0.01f, (DisplayScale / UiScale)) * UiScale;
        float screenWidth = _window.FramebufferSize.X / scale;
        ModalBuilder dialog = Origami.Modal(Loc.Get(title))
            .Width(Math.Min(560, screenWidth - 48))
            .Content(p => p.Box("confirmation-message")
                .Width(UnitValue.Stretch())
                .Height(UnitValue.Auto)
                .Text(Loc.Get(message), _font)
                .FontSize(18)
                .TextColor(Ink)
                .Wrap(TextWrapMode.Wrap)
                .IsNotInteractable())
            .Button(Loc.Get("launcher.common.cancel"), () => Complete(false));

        if (sdkUrl != null)
        {
            dialog.Button(Loc.Get("launcher.sdk.get"), () =>
            {
                Modal.Pop();
                Open(sdkUrl);
                completion.TrySetResult(false);
            });
        }

        _confirmationOpen = true;
        try
        {
            dialog.Button(Loc.Get(accept), () => Complete(true), OrigamiVariant.Primary)
                .OnDismissed(_ => completion.TrySetResult(false))
                .Show();

            return await completion.Task.WaitAsync(token);
        }
        finally
        {
            _confirmationOpen = false;
            // Waiting for a decision is not part of the operation's progress delay.
            _operationStarted = Stopwatch.GetTimestamp();
        }

        void Complete(bool accepted)
        {
            Modal.Pop();
            completion.TrySetResult(accepted);
        }
    }

    private static void Open(string value) => Process.Start(new ProcessStartInfo(value)
    {
        UseShellExecute = true
    });
}
