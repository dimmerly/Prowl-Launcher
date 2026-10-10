using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Prowl.Launcher;

sealed record EditorConsoleRequest(string Executable,
    string WorkingDirectory,
    string[] Arguments,
    string Home,
    string Name,
    string Version);

sealed record EditorConsoleState(DateTimeOffset Started,
    int? ProcessId = null,
    DateTimeOffset? Ready = null,
    DateTimeOffset? Ended = null,
    int? ExitCode = null,
    string? Error = null);

sealed record EditorConsoleLine(DateTimeOffset Time, string Text, bool StandardError)
{
    internal bool IsWarning => Text.Contains("warning", StringComparison.OrdinalIgnoreCase);
    internal bool IsError => StandardError && !IsWarning || Text.Contains("error", StringComparison.OrdinalIgnoreCase)
                                                         || Text.Contains("exception", StringComparison.OrdinalIgnoreCase);
    internal bool IsProblem => StandardError || IsWarning || IsError;
}

sealed record EditorConsoleEvent(EditorConsoleState? State = null, EditorConsoleLine? Line = null);

/// <summary>The collector drains editor output even after the launcher disconnects. No logs are written to disk.</summary>
static class EditorConsoleService
{
    internal static EditorConsoleSession Start(ProcessStartInfo editor, string home, string name, string version)
    {
        string pipeName = PipeName("prowl-editor-output-");
        NamedPipeServerStream pipe = new( pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly );
        EditorConsoleRequest request = new( editor.FileName, editor.WorkingDirectory,
            editor.ArgumentList.ToArray(), home, name, version );
        try
        {
            Process process = Process.Start(CollectorInfo(pipeName)) ?? throw new IOException("Could not start the editor log collector.");
            return new EditorConsoleSession(pipe, request, process);
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    private static string PipeName(string prefix)
    {
        string name = prefix + Guid.NewGuid().ToString("N");
        return OperatingSystem.IsWindows() ? name : Path.Combine("/tmp", name);
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty assembly path selects the single-file apphost.")]
    internal static ProcessStartInfo CollectorInfo(string pipeName)
    {
        string assembly = typeof( Launcher ).Assembly.Location;
        string executable = string.IsNullOrEmpty(assembly) ? Environment.ProcessPath!
            : Path.Combine(Path.GetDirectoryName(assembly)!, "Prowl.Launcher" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        ProcessStartInfo info = new( executable )
        {
            UseShellExecute = false, CreateNoWindow = true
        };
        info.ArgumentList.Add("--editor-console-host");
        info.ArgumentList.Add(pipeName);
        return info;
    }

    internal static async Task<int> CollectAsync(string pipeName)
    {
        using HiddenEditorConsole console = new();
        using NamedPipeClientStream outputPipe = new( ".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly );
        await outputPipe.ConnectAsync(15000);
        using StreamReader input = new( outputPipe, Encoding.UTF8, false, 1024, true );
        EditorConsoleRequest request = JsonSerializer.Deserialize<EditorConsoleRequest>(await input.ReadLineAsync()
                                                                                        ?? throw new IOException("The launcher disconnected before starting the editor."))
                                       ?? throw new InvalidDataException("Missing editor console request.");
        StreamWriter output = new( outputPipe, new UTF8Encoding(false), 1024, true )
        {
            AutoFlush = true
        };
        bool connected = true;
        object outputLock = new();
        void Send(EditorConsoleEvent entry)
        {
            lock (outputLock)
            {
                if (!connected)
                {
                    return;
                }
                try
                {
                    output.WriteLine(JsonSerializer.Serialize(entry));
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException)
                {
                    connected = false;
                }
            }
        }
        void Append(string text, bool error)
        {
            text = CleanOutput(text);
            if (text.Length > 0)
            {
                Send(new EditorConsoleEvent(Line: new EditorConsoleLine(DateTimeOffset.UtcNow, text, error)));
            }
        }
        EditorConsoleState state = new( DateTimeOffset.UtcNow );
        using CancellationTokenSource stopped = new();
        string readyName = PipeName("prowl-editor-ready-");
        using NamedPipeServerStream readyPipe = new( readyName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly );
        ProcessStartInfo info = new( request.Executable )
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = !OperatingSystem.IsWindows(),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.Environment[Constants.Storage.HomeEnvironment] = request.Home;
        info.Environment["PROWL_EDITOR_READY_PIPE"] = readyName;
        foreach (string argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }
        Process? editor = null;
        try
        {
            editor = Process.Start(info) ?? throw new IOException("Could not start the editor.");
            state = state with
            {
                ProcessId = editor.Id
            };
            Send(new EditorConsoleEvent(State: state));
            Task stdout = DrainAsync(editor.StandardOutput, false);
            Task stderr = DrainAsync(editor.StandardError, true);
            Task<bool> ready = ReadReadyAsync(readyPipe, stopped.Token);
            while (!editor.HasExited)
            {
                if (state.Ready == null && (ready.IsCompletedSuccessfully && ready.Result || HasResponsiveWindow(editor)))
                {
                    state = state with
                    {
                        Ready = DateTimeOffset.UtcNow
                    };
                    Send(new EditorConsoleEvent(State: state));
                }
                await Task.Delay(200);
            }
            await Task.WhenAll(stdout, stderr);
            state = state with
            {
                Ended = DateTimeOffset.UtcNow, ExitCode = editor.ExitCode
            };
            Send(new EditorConsoleEvent(State: state));
            return editor.ExitCode;
        }
        catch (Exception error)
        {
            Append(error.Message, true);
            Send(new EditorConsoleEvent(State: state with
            {
                Ended = DateTimeOffset.UtcNow, Error = error.Message
            }));
            return 1;
        }
        finally
        {
            stopped.Cancel();
            editor?.Dispose();
            try
            {
                output.Dispose();
            }
            catch (IOException)
            {
            }
        }

        async Task DrainAsync(StreamReader reader, bool error)
        {
            while (await reader.ReadLineAsync() is {} line)
            {
                Append(line, error);
            }
        }
    }

    // The editor updates Console.Title; give it an attached console without showing a terminal.
    private sealed class HiddenEditorConsole : IDisposable
    {
        private readonly bool _allocated;
        internal HiddenEditorConsole()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            FreeConsole();
            _allocated = AllocConsole();
            if (_allocated)
            {
                ShowWindow(GetConsoleWindow(), 0);
            }
        }
        public void Dispose()
        {
            if (_allocated)
            {
                FreeConsole();
            }
        }
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();
        [DllImport("kernel32.dll")]
        private static extern nint GetConsoleWindow();
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint window, int command);
    }

    private static async Task<bool> ReadReadyAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            await pipe.WaitForConnectionAsync(token);
            byte[] signal = new byte[1];
            return await pipe.ReadAsync(signal, token) == 1 && signal[0] == 1;
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool HasResponsiveWindow(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        try
        {
            process.Refresh();
            nint window = process.MainWindowHandle;
            return window != 0 && SendMessageTimeout(window, 0, 0, 0, 3, 100, out _) != 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);

    internal static string CleanOutput(string text)
    {
        text = Regex.Replace(text, @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))", "");
        text = new string(text.Where(c => !char.IsControl(c) || c == '\t').ToArray());
        return text.Length > 16384 ? text[..16384] + "…" : text;
    }
}

sealed class EditorConsoleSession : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _collector;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Queue<EditorConsoleLine> _lines = new();
    private readonly Queue<EditorConsoleLine> _problems = new();
    private EditorConsoleState _state = new( DateTimeOffset.UtcNow );
    private int _revision;
    private int _observedRevision = -1;
    internal string Id
    {
        get;
    } = Guid.NewGuid().ToString("N");
    internal EditorConsoleRequest Request
    {
        get;
    }
    internal EditorConsoleState State
    {
        get;
        private set;
    } = new( DateTimeOffset.UtcNow );
    internal List<EditorConsoleLine> Lines
    {
        get;
    } = [];
    internal List<EditorConsoleLine> Problems
    {
        get;
    } = [];
    internal bool CloseWhenReady
    {
        get;
        set;
    }
    internal bool ReadyHandled
    {
        get;
        set;
    }
    internal bool Active => State.Ended == null;
    internal double Elapsed => Math.Max(0, ((State.Ready ?? State.Ended ?? DateTimeOffset.UtcNow) - State.Started).TotalSeconds);
    internal Task Completion
    {
        get;
    }

    internal EditorConsoleSession(NamedPipeServerStream pipe, EditorConsoleRequest request, Process collector)
    {
        _pipe = pipe;
        Request = request;
        _collector = collector;
        Completion = Task.Run(ReceiveAsync);
    }

    private async Task ReceiveAsync()
    {
        try
        {
            await _pipe.WaitForConnectionAsync(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(15), _lifetime.Token);
            using StreamWriter writer = new( _pipe, new UTF8Encoding(false), 1024, true )
            {
                AutoFlush = true
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(Request));
            using StreamReader reader = new( _pipe, Encoding.UTF8, false, 1024, true );
            while (await reader.ReadLineAsync(_lifetime.Token) is {} json)
            {
                EditorConsoleEvent? entry = JsonSerializer.Deserialize<EditorConsoleEvent>(json);
                lock (_gate)
                {
                    _revision++;
                    if (entry?.State is {} state)
                    {
                        _state = state;
                    }
                    if (entry?.Line is {} line)
                    {
                        _lines.Enqueue(line);
                        if (_lines.Count > 1000)
                        {
                            _lines.Dequeue();
                        }
                        if (line.IsProblem)
                        {
                            _problems.Enqueue(line);
                            if (_problems.Count > 1000)
                            {
                                _problems.Dequeue();
                            }
                        }
                    }
                }
            }
            lock (_gate)
            {
                if (_state.Ended == null)
                {
                    _state = _state with
                    {
                        Ended = DateTimeOffset.UtcNow, Error = "The editor log collector disconnected."
                    };
                    _revision++;
                }
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or JsonException or TimeoutException)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                lock (_gate)
                {
                    _state = _state with
                    {
                        Ended = DateTimeOffset.UtcNow, Error = error.Message
                    };
                    _revision++;
                }
            }
        }
        finally
        {
            _pipe.Dispose();
        }
    }

    internal void Refresh()
    {
        lock (_gate)
        {
            if (_observedRevision == _revision)
            {
                return;
            }
            _observedRevision = _revision;
            State = _state;
            Lines.Clear();
            Lines.AddRange(_lines);
            Problems.Clear();
            Problems.AddRange(_problems);
        }
    }

    internal async Task WaitForStartAsync(CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        do
        {
            await Task.Delay(100, deadline.Token);
            Refresh();
        } while (State.ProcessId == null && State.Ended == null);
        if (State.Error != null)
        {
            throw new IOException(State.Error);
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _pipe.Dispose();
        _collector.Dispose();
    }
}
