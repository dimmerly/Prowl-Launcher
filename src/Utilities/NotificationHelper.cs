using System.Collections.Concurrent;

using Prowl.Rosetta;
using Prowl.OrigamiUI;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private readonly ConcurrentQueue<(string Title, string Message, ToastType Type)> _notifications = new();

    private void ShowPendingNotifications()
    {
        while (_notifications.TryDequeue(out (string Title, string Message, ToastType Type) notification))
        {
            float duration = notification.Type == ToastType.Error ? 6 : 4;
            Toasts.Show(
                Loc.Get(notification.Title),
                Loc.Get(notification.Message),
                notification.Type,
                duration);
        }
    }

    private void Notify(string title, string message, ToastType type = ToastType.Success) => _notifications.Enqueue((title, message, type));
}
