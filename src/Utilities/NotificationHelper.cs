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
        OrigamiTheme theme = _appearance.Theme;
        OrigamiMetrics metrics = theme.Metrics;
        if (theme.Font is not {} font)
        {
            return;
        }
        for (int i = _activeToasts.Count - 1; i >= 0; i--)
        {
            NotificationToast toast = _activeToasts[i];
            toast.Remaining -= _deltaTime;
            if (toast.Remaining <= 0)
            {
                _activeToasts.RemoveAt(i);
            }
        }

        float availableWidth = Math.Max(0, (float)p.ScreenRect.Size.X - metrics.PaddingLarge * 2);
        float textWidth = Math.Max(0, availableWidth - metrics.PaddingLarge * 2
            - metrics.CompactHeight * 2 - metrics.SpacingLarge * 2);
        using (p.Column("notifications")
            .PositionType(PositionType.SelfDirected)
            .AnchorRight(metrics.PaddingLarge)
            .AnchorBottom(metrics.PaddingLarge + (progressVisible ? Constants.Layout.OperationHeight : 0))
            .Size(UnitValue.Auto)
            .MaxWidth(availableWidth)
            .AlignItems(LayoutAlignment.End)
            .Gap(metrics.SpacingLarge)
            .Layer(Layer.Overlay + 100000)
            .Enter())
        {
            foreach (NotificationToast toast in _activeToasts)
            {
                string id = "notification-" + toast.Id;
                var semantic = toast.Type switch
                {
                    ToastType.Success => theme.Green.C500,
                    ToastType.Warning => theme.Amber.C500,
                    ToastType.Error => theme.Red.C500,
                    _ => theme.Blue.C500
                };
                var icon = toast.Type switch
                {
                    ToastType.Success => theme.Icons.Check,
                    ToastType.Warning => theme.Icons.Warning,
                    ToastType.Error => theme.Icons.Close,
                    _ => theme.Icons.Info
                };
                using (p.Box(id).Size(UnitValue.Auto).StopEventPropagation().Enter())
                {
                    using (p.Row(id + "-card")
                        .Size(UnitValue.Auto)
                        .Padding(metrics.PaddingLarge)
                        .Gap(metrics.SpacingLarge)
                        .AlignItems(LayoutAlignment.Center)
                        .BackgroundColor(theme.Popover)
                        .BorderColor(theme.BorderStrong).BorderWidth(1)
                        .Rounded(metrics.ContainerRounding)
                        .Enter())
                    {
                        p.Box(id + "-icon")
                            .Size(metrics.CompactHeight)
                            .Rounded(metrics.Rounding)
                            .BackgroundColor(OrigamiTheme.WithAlpha(semantic, 38))
                            .IsNotInteractable()
                            .Icon(p, icon, semantic, size: metrics.FontSizeSmall);
                        using (p.Column(id + "-text").Size(UnitValue.Auto).Gap(metrics.SpacingSmall).Enter())
                        {
                            p.Box(id + "-title").Size(UnitValue.Auto).MaxWidth(textWidth)
                                .Text(toast.Count > 1 ? $"{toast.Title} (x{toast.Count})" : toast.Title, theme.Medium ?? font)
                                .FontSize(metrics.FontSize).TextColor(theme.Ink.C500)
                                .Wrap(Prowl.Scribe.TextWrapMode.Wrap).IsNotInteractable();
                            if (toast.Message.Length > 0)
                            {
                                p.Box(id + "-message").Size(UnitValue.Auto).MaxWidth(textWidth)
                                    .Text(toast.Message, font)
                                    .FontSize(metrics.FontSizeSmall).TextColor(theme.Ink.C200)
                                    .Wrap(Prowl.Scribe.TextWrapMode.Wrap).IsNotInteractable();
                            }
                        }
                        p.Box(id + "-close")
                            .Size(metrics.CompactHeight)
                            .Cursor(PaperCursor.Pointer)
                            .Tooltip(Loc.Get("launcher.common.close"))
                            .OnClick(_ => _activeToasts.Remove(toast))
                            .Icon(p, theme.Icons.Close, theme.Ink.C200, size: metrics.FontSizeSmall);
                    }
                }
            }
        }
    }

    private void Notify(string title, string message, ToastType type = ToastType.Success) => _notifications.Enqueue((title, message, type));
}
