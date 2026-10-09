using System.Collections.Concurrent;

namespace Prowl.Launcher;

sealed class UiContext : SynchronizationContext, IDisposable
{
    private readonly SynchronizationContext? _previous = Current;
    private readonly ConcurrentQueue<(SendOrPostCallback callback, object? state)> _queue = new();
    private bool _disposed;

    public UiContext() => SetSynchronizationContext(this);

    public override void Post(SendOrPostCallback callback, object? state)
    {
        if (!_disposed)
        {
            _queue.Enqueue((callback, state));
        }
    }

    public void Pump()
    {
        while (_queue.TryDequeue(out (SendOrPostCallback callback, object? state) entry))
        {
            entry.callback(entry.state);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _queue.Clear();
        SetSynchronizationContext(_previous);
    }
}
