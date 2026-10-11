using System.Diagnostics;

using Prowl.OrigamiUI;
using Prowl.Rosetta;
namespace Prowl.Launcher;

/// <summary>
///     The single foreground operation (install, launch, update, ...). Only one runs at a time; it drives the
///     bottom progress panel and reports failures as toasts.
/// </summary>
public sealed partial class Launcher
{

    private bool Busy => _operation != null;

    /// <summary>
    ///     The progress panel appears only after a short delay (so quick operations don't flash it), unless the
    ///     operation asked for it immediately. It stays hidden while a picker or confirmation is waiting on the user.
    /// </summary>
    private bool ShowProgress
    {
        get
        {
            if (!Busy || _tab == LauncherTab.Launch || _filePickerOpen || _confirmationOpen)
            {
                return false;
            }
            if (_launchingSampleId != null && !_immediateProgress)
            {
                return false; // The sample card shows its own spinner.
            }
            return _immediateProgress
                   || Stopwatch.GetElapsedTime(_operationStarted).TotalMilliseconds >= Constants.Layout.ProgressDelayMilliseconds;
        }
    }
    private bool _closeAfterCancel;
    private bool _confirmationOpen;
    private bool _filePickerOpen;
    private double? _fraction;
    private bool _immediateProgress;
    private CancellationTokenSource? _operation;
    private string _operationDetail = "";
    private long _operationStarted;
    private string _operationTitle = "launcher.operations.working_title";
    private string? _restart;

    private async void Start(Func<CancellationToken, Task> action, string title)
    {
        if (Busy)
        {
            return;
        }

        _operation = new CancellationTokenSource();
        _restart = null;
        _operationStarted = Stopwatch.GetTimestamp();
        _immediateProgress = false;
        _fraction = null;
        _operationTitle = title;
        _operationDetail = Loc.Get(title) + "…";
        try
        {
            await action(_operation.Token);
            if (_restart != null)
            {
                Process.Start(new ProcessStartInfo(_restart)
                {
                    UseShellExecute = false
                });
                _closeAfterCancel = true;
            }
        }
        catch (OperationCanceledException)
        {
            Notify("launcher.operations.cancelled", "launcher.operations.installations_unchanged", ToastType.Info);
        }
        catch (Exception exception)
        {
            Notify(Loc.Get("launcher.operations.failed", new
            {
                action = Loc.Get(title)
            }), exception.Message, ToastType.Error);
            LogError(exception);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            // An exception escaping this async void method would terminate the launcher.
            try
            {
                ReloadInstalled();
            }
            catch (Exception exception)
            {
                LogError(exception);
            }
            if (_closeAfterCancel)
            {
                ForceClose();
            }
        }
    }

    /// <summary>A progress sink that feeds the operation panel.</summary>
    private IProgress<TransferProgress> Transfer() => new Progress<TransferProgress>(progress =>
    {
        _operationDetail = progress.Message;
        _fraction = progress.Fraction;
    });

    /// <summary>Shows a folder picker; waiting on the user doesn't count towards the progress delay.</summary>
    private async Task<string?> PickFolderAsync(string initialDirectory, CancellationToken token)
    {
        TaskCompletionSource<string?> completion = new();
        _filePickerOpen = true;
        try
        {
            Directory.CreateDirectory(initialDirectory);
            Origami.OpenFileDialog(FileDialogMode.SelectFolder, selected => completion.TrySetResult(selected), initialDirectory);
            return await completion.Task.WaitAsync(token);
        }
        finally
        {
            _filePickerOpen = false;
            _operationStarted = Stopwatch.GetTimestamp();
        }
    }

    private void LogError(Exception exception) => LauncherLog.Append(store.Home, exception);
}
