using System.Diagnostics;

using Prowl.OrigamiUI;
using Prowl.Rosetta;

namespace Prowl.Launcher;

public sealed partial class Launcher
{
    private CancellationTokenSource? _operation;
    private string _operationDetail = "";
    private string _operationTitle = "launcher.operations.working_title";
    private double? _fraction;
    private long _operationStarted;
    private bool _immediateProgress;
    private bool _filePickerOpen;
    private bool _confirmationOpen;
    private bool _closeAfterCancel;
    private string? _restart;

    private bool Busy => _operation != null;
    private bool ShowProgress => Busy
                                 && _tab != 4
                                 && (_launchingSampleId == null || _immediateProgress)
                                 && !_filePickerOpen
                                 && !_confirmationOpen
                                 && (_immediateProgress || Stopwatch.GetElapsedTime(_operationStarted).TotalMilliseconds >= Constants.Layout.ProgressDelayMilliseconds);

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
            ReloadInstalled();
            if (_closeAfterCancel)
            {
                ForceClose();
            }
        }
    }

    private IProgress<TransferProgress> Transfer() => new Progress<TransferProgress>(progress =>
    {
        _operationDetail = progress.Message;
        _fraction = progress.Fraction;
    });

    private async Task RunSampleAsync(Sample sample, CancellationToken token)
    {
        _launchingSampleId = sample.Id;
        try
        {
            _immediateProgress = !_samples.IsCached;
            if (_immediateProgress)
            {
                _operationTitle = "launcher.samples.downloading";
            }
            await _samples.EnsureDownloadedAsync(Transfer(), token, !offline);
            _immediateProgress = false;
            _operationTitle = "launcher.samples.run";
            await _samples.RunAsync(sample, Transfer(), token, !offline);
        }
        finally
        {
            _launchingSampleId = null;
        }
    }

    private void LogError(Exception exception)
    {
        try
        {
            File.AppendAllText(Path.Combine(store.Home, "launcher.log"), $"{DateTimeOffset.UtcNow:o} {exception}\n");
        }
        catch (IOException)
        {
        }
    }
}
