using System.Diagnostics;
using System.IO.Pipes;
using Xunit;

namespace Prowl.Launcher.Test;

[Trait("Category", "Integration")]
public sealed class EditorConsoleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ProwlConsoleTests", Guid.NewGuid().ToString("N"));

    private EditorConsoleSession Start(params string[] args)
    {
        ProcessStartInfo info = new(Path.Combine(AppContext.BaseDirectory,
            "Prowl.Launcher.Test" + (OperatingSystem.IsWindows() ? ".exe" : "")))
        {
            WorkingDirectory = AppContext.BaseDirectory
        };
        info.ArgumentList.Add("--console-probe");
        foreach (string argument in args) info.ArgumentList.Add(argument);
        return EditorConsoleService.Start(info, _root, "Large project", "v1.0.0");
    }

    [Fact]
    public async Task CapturesBothStreamsWaitsForReadyAndBoundsTheVisibleLog()
    {
        using EditorConsoleSession session = Start();
        await session.WaitForStartAsync(CancellationToken.None);
        Assert.NotNull(session.State.ProcessId);
        Assert.Null(session.State.Ready);
        await Wait(() => { session.Refresh(); return session.State.Ready != null; });
        Assert.True(session.Active);
        Assert.Contains(session.Lines, line => line.StandardError && line.Text.Contains("missing material"));
        Assert.Contains(session.Lines, line => line.Text == "Loading models");
        await Wait(() => { session.Refresh(); return !session.Active && session.Lines.Any(l => l.Text == "Final editor output"); });
        Assert.Equal(0, session.State.ExitCode);
        Assert.InRange(session.Lines.Count, 1, 1000);
        Assert.Equal("Final editor output", session.Lines.Last().Text);
        Assert.Contains(session.Problems, line => line.Text.Contains("missing material"));
        Assert.False(Directory.Exists(_root), "Editor console capture must not create files or folders.");
    }

    [Fact]
    public async Task ClosingTheViewLeavesTheCollectorAndEditorRunning()
    {
        string pipeName = "prowl-console-finished-" + Guid.NewGuid().ToString("N");
        if (!OperatingSystem.IsWindows()) pipeName = Path.Combine("/tmp", pipeName);
        using NamedPipeServerStream completion = new(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        EditorConsoleSession session = Start("completed-pipe=" + pipeName);
        await session.WaitForStartAsync(CancellationToken.None);
        session.Dispose();
        await completion.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(15));
        byte[] signal = new byte[1];
        Assert.Equal(1, await completion.ReadAsync(signal).AsTask().WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(1, signal[0]);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task MissingExecutableReportsAStartupFailureInTheSession()
    {
        using EditorConsoleSession session = EditorConsoleService.Start(new ProcessStartInfo(Path.Combine(_root, "missing-editor")),
            _root, "Missing editor", "v1.0.0");
        await Assert.ThrowsAsync<IOException>(() => session.WaitForStartAsync(CancellationToken.None));
        Assert.False(session.Active);
        Assert.Null(session.State.Ready);
        Assert.Contains(session.Lines, line => line.StandardError);
    }

    [Fact]
    public async Task EarlyCrashRetainsDiagnosticsAndDoesNotReportReady()
    {
        using EditorConsoleSession session = Start("fail");
        await Wait(() => { session.Refresh(); return !session.Active; });
        Assert.Equal(7, session.State.ExitCode);
        Assert.Null(session.State.Ready);
        Assert.Contains(session.Lines, line => line.StandardError);
    }

    [Theory]
    [InlineData("\u001b[31mError\u001b[0m\r", "Error")]
    [InlineData("\u001b]0;title\u0007Hello\tworld", "Hello\tworld")]
    public void RemovesTerminalFormatting(string input, string expected)
        => Assert.Equal(expected, EditorConsoleService.CleanOutput(input));

    private static async Task Wait(Func<bool> predicate)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!predicate())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Editor collector did not finish.");
            await Task.Delay(100);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
