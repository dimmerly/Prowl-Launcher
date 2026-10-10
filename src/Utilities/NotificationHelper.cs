using System.Collections.Concurrent;

using Prowl.Rosetta;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private readonly ConcurrentQueue<(string Title, string Message, ToastType Type)> _notifications = new();
    private readonly List<NotificationToast> _activeToasts = [];
    private int _nextToastId;

    private sealed class NotificationToast(int id, string title, string message, ToastType type, float duration)
    {
        public int Id = id;
        public string Title = title;
        public string Message = message;
        public ToastType Type = type;
        public float Remaining = duration;
        public int Count = 1;
    }

    private void ShowPendingNotifications()
    {
        while (_notifications.TryDequeue(out (string Title, string Message, ToastType Type) notification))
        {
            float duration = notification.Type == ToastType.Error ? 6 : 4;
            string title = Loc.Get(notification.Title), message = Loc.Get(notification.Message);
            NotificationToast? existing = _activeToasts.Find(t => t.Title == title && t.Message == message && t.Type == notification.Type);
            if (existing != null)
            {
                existing.Count++;
                existing.Remaining = duration;
                _activeToasts.Remove(existing);
                _activeToasts.Add(existing);
            }
            else
            {
                _activeToasts.Add(new NotificationToast(++_nextToastId, title, message, notification.Type, duration));
            }
        }
    }

    private void DrawNotifications(Paper p, bool progressVisible)
    {
        OrigamiMetrics metrics = _appearance.Theme.Metrics;
        float bottom = (float)p.ScreenRect.Size.Y - metrics.PaddingLarge;
        if (progressVisible)
        {
            bottom -= Constants.Layout.OperationHeight;
        }
        for (int i = _activeToasts.Count - 1; i >= 0; i--)
        {
            NotificationToast toast = _activeToasts[i];
            toast.Remaining -= _deltaTime;
            if (toast.Remaining <= 0)
            {
                _activeToasts.RemoveAt(i);
                continue;
            }

            float textHeight = metrics.FontSize + 2 + (toast.Message.Length > 0 ? 2 + metrics.FontSizeSmall : 0);
            float height = Math.Max(26, textHeight) + 22;
            bottom -= height + 9;
            string id = "notification-" + toast.Id;
            using (p.Box(id)
                .PositionType(PositionType.SelfDirected)
                .Left((float)p.ScreenRect.Size.X - 300 - metrics.PaddingLarge)
                .Top(bottom).Size(300, height)
                .Layer(Layer.Overlay + 100000)
                .StopEventPropagation()
                .Enter())
            {
                Toasts.Preview(p, id + "-card", toast.Count > 1 ? $"{toast.Title} (x{toast.Count})" : toast.Title, toast.Type, toast.Message);
                p.Box(id + "-close")
                    .PositionType(PositionType.SelfDirected)
                    .Left(266).Top((height - 28) / 2).Size(28, 28)
                    .Cursor(PaperCursor.Pointer)
                    .Tooltip(Loc.Get("launcher.common.close"))
                    .OnClick(_ => _activeToasts.Remove(toast));
            }
        }
    }

    private void Notify(string title, string message, ToastType type = ToastType.Success) => _notifications.Enqueue((title, message, type));
}
