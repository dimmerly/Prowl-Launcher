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
                                 && _launchingSampleId == null
                                 && !_filePickerOpen
                                 && !_confirmationOpen
                                 && (_immediateProgress || Stopwatch.GetElapsedTime(_operationStarted).TotalMilliseconds >= 650);

    private async void Start(Func<CancellationToken, Task> action, string title)
    {
        if (Busy)
        {
            return;
        }

        _operation = new CancellationTokenSource();
        _operationStarted = Stopwatch.GetTimestamp();
        _immediateProgress = false;
        _fraction = null;
        _operationTitle = title;
        _operationDetail = Loc.Get(title) + "…";
        try
        {
            await action(_operation.Token);
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
            if (_restart != null)
            {
                Process.Start(new ProcessStartInfo(_restart)
                {
                    UseShellExecute = false
                });
                ForceClose();
            }
            else if (_closeAfterCancel)
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
            await SampleService.RunAsync(sample, store.WorkPath, token);
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
