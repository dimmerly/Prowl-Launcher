using System.Diagnostics;

using Prowl.OrigamiUI;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>
///     The single foreground operation (install, launch, update, ...). Only one runs at a time; it drives the
///     bottom progress panel and reports failures as toasts.
/// </summary>
sealed class OperationCoordinator(NotificationCenter notifications, Action<Exception> logError) : IDisposable
{
    private CancellationTokenSource? _operation;
    private long _started;
    private int _waitingOnUser;

    public bool Busy => _operation != null;
    public bool Cancelling => _operation?.IsCancellationRequested == true;

    /// <summary>A localization key or display text for the progress panel title.</summary>
    public string Title { get; set; } = "launcher.operations.working_title";

    public string Detail { get; set; } = "";
    public double? Fraction { get; set; }

    /// <summary>Shows the progress panel right away instead of after the usual delay.</summary>
    public bool ImmediateProgress { get; set; }

    /// <summary>An executable to start once the current operation succeeds; the launcher then closes.</summary>
    public string? RestartWith { get; set; }

    /// <summary>Closes the launcher once the current operation ends.</summary>
    public bool CloseWhenDone { get; set; }

    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);

    /// <summary>
    ///     The progress panel appears only after a short delay (so quick operations don't flash it), unless the
    ///     operation asked for it immediately. It stays hidden while a picker or confirmation is waiting on the user.
    /// </summary>
    public bool ProgressDue => Busy && _waitingOnUser == 0
                               && (ImmediateProgress || Elapsed.TotalMilliseconds >= Constants.Layout.ProgressDelayMilliseconds);

    /// <summary>Raised after every operation, whether it succeeded or not.</summary>
    public event Action? Finished;

    /// <summary>Raised when an operation ends with <see cref="CloseWhenDone" /> set.</summary>
    public event Action? CloseRequested;

    public void Dispose() => _operation?.Dispose();

    public async void Start(Func<CancellationToken, Task> action, string title)
    {
        if (Busy)
        {
            return;
        }

        _operation = new CancellationTokenSource();
        RestartWith = null;
        _started = Stopwatch.GetTimestamp();
        ImmediateProgress = false;
        Fraction = null;
        Title = title;
        Detail = Loc.Get(title) + "…";
        try
        {
            await action(_operation.Token);
            if (RestartWith != null)
            {
                Process.Start(new ProcessStartInfo(RestartWith)
                {
                    UseShellExecute = false
                });
                CloseWhenDone = true;
            }
        }
        catch (OperationCanceledException)
        {
            notifications.Notify("launcher.operations.cancelled", "launcher.operations.installations_unchanged", ToastType.Info);
        }
        catch (Exception exception)
        {
            notifications.Notify(Loc.Get("launcher.operations.failed", new
            {
                action = Loc.Get(title)
            }), exception.Message, ToastType.Error);
            logError(exception);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            // An exception escaping this async void method would terminate the launcher.
            try
            {
                Finished?.Invoke();
            }
            catch (Exception exception)
            {
                logError(exception);
            }
            if (CloseWhenDone)
            {
                CloseRequested?.Invoke();
            }
        }
    }

    public void Cancel() => _operation?.Cancel();

    /// <summary>Shows a fixed, never-ending operation for screenshots.</summary>
    public void ShowPreview(string title, string detail, double fraction)
    {
        _operation = new CancellationTokenSource();
        ImmediateProgress = true;
        Title = title;
        Detail = detail;
        Fraction = fraction;
    }

    /// <summary>A progress sink that feeds the operation panel.</summary>
    public IProgress<TransferProgress> Transfer() => new Progress<TransferProgress>(progress =>
    {
        Detail = progress.Message;
        Fraction = progress.Fraction;
    });

    /// <summary>
    ///     Marks the operation as waiting on the user until disposed; that time doesn't count towards the
    ///     progress delay.
    /// </summary>
    public IDisposable WaitForUser()
    {
        _waitingOnUser++;
        return new UserWait(this);
    }

    private sealed class UserWait(OperationCoordinator owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            owner._waitingOnUser--;
            owner._started = Stopwatch.GetTimestamp();
        }
    }
}
