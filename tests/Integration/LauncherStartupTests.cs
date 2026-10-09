using System.Diagnostics;
using System.IO.Pipes;

using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class LauncherStartupTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ProwlStartupTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void InvalidUpdateFallsBackAndClearsTheSavedTarget()
    {
        LauncherStore store = new( _home );
        string executable = Path.Combine(_home, "LauncherVersions", "broken", "Prowl.Launcher.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "invalid executable");
        store.Settings.LauncherExecutable = executable;
        store.Save();
        Assert.False(Program.ForwardToUpdatedLauncher(store, []));
        Assert.Null(new LauncherStore(_home).Settings.LauncherExecutable);
    }

    [Fact]
    public async Task EarlyExitCannotPassStartupHealthCheck() => await Assert.ThrowsAsync<IOException>(() => LauncherStartupService.StartAsync(
        "unused", _home, [], start: _ => StartHelper("exit 0")));

    [Fact]
    public async Task UnresponsiveChildIsTerminatedOnTimeout()
    {
        int childId = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LauncherStartupService.StartAsync(
            "unused", _home, [], timeout: TimeSpan.FromMilliseconds(300),
            start: _ =>
            {
                Process child = StartHelper(OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 30" : "sleep 30");
                childId = child.Id;
                return child;
            }));
        try
        {
            using Process child = Process.GetProcessById(childId);
            Assert.True(child.WaitForExit(5000));
        }
        catch (ArgumentException)
        {
            /* The process has already been reaped. */
        }
    }

    [Fact]
    public async Task HealthyChildAcknowledgesReadinessAndReceivesArgumentsAndHome()
    {
        int childId = 0;
        Task? acknowledgement = null;
        await LauncherStartupService.StartAsync("unused", _home, ["--offline"], start: info =>
        {
            Assert.Equal(_home, info.Environment["PROWL_LAUNCHER_HOME"]);
            Assert.Equal(new[]
            {
                "--offline"
            }, info.ArgumentList);
            string name = info.Environment["PROWL_LAUNCHER_READY_PIPE"]!;
            acknowledgement = Task.Run(async () =>
            {
                await using NamedPipeClientStream pipe = new( ".", name, PipeDirection.Out, PipeOptions.Asynchronous );
                await pipe.ConnectAsync(5000);
                await pipe.WriteAsync(new byte[]
                {
                    1
                });
                await pipe.FlushAsync();
            });
            Process child = StartHelper(OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 30" : "sleep 30");
            childId = child.Id;
            return child;
        });
        try
        {
            await acknowledgement!;
            using Process child = Process.GetProcessById(childId);
            Assert.False(child.HasExited);
        }
        finally
        {
            using Process child = Process.GetProcessById(childId);
            child.Kill(entireProcessTree: true);
        }
    }

    private static Process StartHelper(string command)
    {
        ProcessStartInfo info = new( OperatingSystem.IsWindows() ? "powershell" : "/bin/sh" )
        {
            UseShellExecute = false, CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
        }
        else
        {
            info.ArgumentList.Add("-c");
        }
        info.ArgumentList.Add(command);
        return Process.Start(info)!;
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, true);
        }
    }
}
