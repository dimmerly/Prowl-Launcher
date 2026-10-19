using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;

namespace Prowl.Launcher;

/// <summary>What the launcher asks the collector to start.</summary>
internal sealed record EditorConsoleRequest(
    string Executable,
    string WorkingDirectory,
    string[] Arguments,
    string Home,
    string Name,
    string Version);

internal sealed record EditorConsoleState(
    DateTimeOffset Started,
    int? ProcessId = null,
    DateTimeOffset? Ready = null,
    DateTimeOffset? Ended = null,
    int? ExitCode = null,
    string? Error = null);

internal sealed record EditorConsoleLine(DateTimeOffset Time, string Text, bool StandardError)
{
    internal bool IsWarning => Text.Contains("warning", StringComparison.OrdinalIgnoreCase);

    internal bool IsError => (StandardError && !IsWarning)
                             || Text.Contains("error", StringComparison.OrdinalIgnoreCase)
                             || Text.Contains("exception", StringComparison.OrdinalIgnoreCase);

    internal bool IsProblem => StandardError || IsWarning || IsError;
}

/// <summary>One message from the collector: a state change or an output line.</summary>
internal sealed record EditorConsoleEvent(EditorConsoleState? State = null, EditorConsoleLine? Line = null);

/// <summary>
///     Launches editors through a collector process (this executable with <see cref="CollectorArgument" />),
///     so editor output keeps draining after the launcher closes. No logs are written to disk.
/// </summary>
internal static class EditorConsoleService
{
    internal const string CollectorArgument = "--editor-console-host";

    internal static EditorConsoleSession Start(ProcessStartInfo editor, string home, string name, string version)
    {
        string pipeName = ProcessHelper.PipeName("prowl-editor-output-");
        NamedPipeServerStream pipe = new(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        EditorConsoleRequest request = new(editor.FileName, editor.WorkingDirectory,
            editor.ArgumentList.ToArray(), home, name, version);
        try
        {
            Process process = Process.Start(CollectorInfo(pipeName)) ??
                              throw new IOException("Could not start the editor log collector.");
            return new EditorConsoleSession(pipe, request, process);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "An empty assembly path selects the single-file apphost.")]
    internal static ProcessStartInfo CollectorInfo(string pipeName)
    {
        string assembly = typeof(Launcher).Assembly.Location;
        string executable = string.IsNullOrEmpty(assembly)
            ? Environment.ProcessPath!
            : Path.Combine(Path.GetDirectoryName(assembly)!, Platform.ExecutableName("Prowl.Launcher"));
        ProcessStartInfo info = new(executable) { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(CollectorArgument);
        info.ArgumentList.Add(pipeName);
        return info;
    }
}
