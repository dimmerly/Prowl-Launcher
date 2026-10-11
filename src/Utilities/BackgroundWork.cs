namespace Prowl.Launcher;

/// <summary>
///     Cancellation for quiet background work: release, news, sample and storage checks. <see cref="Restart" />
///     cancels everything in flight while later work keeps running; read <see cref="Token" /> when work starts.
/// </summary>
sealed class BackgroundWork : IDisposable
{
    private CancellationTokenSource _cancellation = new();

    public CancellationToken Token => _cancellation.Token;

    /// <summary>Cancels work in flight, e.g. so results from an old repository can't land after a switch.</summary>
    public void Restart()
    {
        CancellationTokenSource previous = Interlocked.Exchange(ref _cancellation, new CancellationTokenSource());
        // Not disposed: work in flight may still register on its token.
        previous.Cancel();
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
