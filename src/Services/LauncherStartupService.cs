using System.Diagnostics;
using System.IO.Pipes;

namespace Prowl.Launcher;

internal static class LauncherStartupService
{
    private const string ReadyEnvironment = "PROWL_LAUNCHER_READY_PIPE";
    internal static string? ReadyPipe { get; private set; }

    internal static void CaptureReadyPipe()
    {
        ReadyPipe = Environment.GetEnvironmentVariable(ReadyEnvironment);
        Environment.SetEnvironmentVariable(ReadyEnvironment, null);
    }

    internal static async Task ReportReadyAsync()
    {
        string? name = ReadyPipe;
        ReadyPipe = null;
        if (name == null) return;
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            await using NamedPipeClientStream pipe = new(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException)
        {
            // The original launcher may have exited while this window was loading.
        }
    }

    internal static async Task StartAsync(string executable, string home, IEnumerable<string> args,
        CancellationToken token = default, TimeSpan? timeout = null,
        Func<ProcessStartInfo, Process?>? start = null)
    {
        string name = "prowl-ready-" + Guid.NewGuid().ToString("N");
        // Absolute Unix pipe paths avoid adding a potentially long TMPDIR and CoreFxPipe_ prefix.
        if (!OperatingSystem.IsWindows()) name = Path.Combine("/tmp", name);
        await using NamedPipeServerStream pipe = new(name, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        ProcessStartInfo info = new(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        info.Environment[ReadyEnvironment] = name;
        info.Environment["PROWL_LAUNCHER_HOME"] = home;
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using Process process = (start ?? Process.Start)(info)
            ?? throw new IOException("The updated launcher could not be started.");
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            Task ready = WaitForReadyAsync(pipe, deadline.Token);
            Task exited = process.WaitForExitAsync(deadline.Token);
            if (await Task.WhenAny(ready, exited) == exited)
            {
                await exited;
                throw new IOException("The updated launcher exited before its window was ready.");
            }
            await ready;
            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
            if (process.HasExited)
                throw new IOException("The updated launcher failed during startup.");
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
        finally { deadline.Cancel(); }
    }

    private static async Task WaitForReadyAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        await pipe.WaitForConnectionAsync(token);
        byte[] response = new byte[1];
        if (await pipe.ReadAsync(response, token) != 1 || response[0] != 1)
            throw new IOException("The updated launcher did not acknowledge startup.");
    }
}
