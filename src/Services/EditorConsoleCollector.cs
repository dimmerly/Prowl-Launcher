using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Prowl.Launcher;

/// <summary>
///     Runs in the collector process: starts the editor, forwards its output and state to the launcher over a
///     pipe, and keeps draining output if the launcher disconnects.
/// </summary>
static class EditorConsoleCollector
{
    private const int MaxLineLength = 16384;

    internal static async Task<int> RunAsync(string pipeName)
    {
        using HiddenConsole console = new();
        using NamedPipeClientStream pipe = new( ".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly );
        await pipe.ConnectAsync(15000);
        using StreamReader input = new( pipe, Encoding.UTF8, false, 1024, true );
        string requestJson = await input.ReadLineAsync()
                             ?? throw new IOException("The launcher disconnected before starting the editor.");
        EditorConsoleRequest request = JsonSerializer.Deserialize<EditorConsoleRequest>(requestJson)
                                       ?? throw new InvalidDataException("Missing editor console request.");

        using Connection launcher = new( pipe );
        EditorConsoleState state = new( DateTimeOffset.UtcNow );
        using CancellationTokenSource stopped = new();
        string readyName = ProcessHelper.PipeName("prowl-editor-ready-");
        using NamedPipeServerStream readyPipe = new( readyName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly );
        Process? editor = null;
        try
        {
            editor = Process.Start(EditorInfo(request, readyName)) ?? throw new IOException("Could not start the editor.");
            state = state with
            {
                ProcessId = editor.Id
            };
            launcher.Send(new EditorConsoleEvent(State: state));

            Task stdout = DrainAsync(editor.StandardOutput, false);
            Task stderr = DrainAsync(editor.StandardError, true);
            Task<bool> ready = ReadReadyAsync(readyPipe, stopped.Token);
            while (!editor.HasExited)
            {
                bool signalled = ready.IsCompletedSuccessfully && ready.Result;
                if (state.Ready == null && (signalled || HasResponsiveWindow(editor)))
                {
                    state = state with
                    {
                        Ready = DateTimeOffset.UtcNow
                    };
                    launcher.Send(new EditorConsoleEvent(State: state));
                }
                await Task.Delay(200);
            }
            await Task.WhenAll(stdout, stderr);

            state = state with
            {
                Ended = DateTimeOffset.UtcNow, ExitCode = editor.ExitCode
            };
            launcher.Send(new EditorConsoleEvent(State: state));
            return editor.ExitCode;
        }
        catch (Exception error)
        {
            launcher.SendLine(error.Message, true);
            launcher.Send(new EditorConsoleEvent(State: state with
            {
                Ended = DateTimeOffset.UtcNow, Error = error.Message
            }));
            return 1;
        }
        finally
        {
            stopped.Cancel();
            editor?.Dispose();
        }

        async Task DrainAsync(StreamReader reader, bool error)
        {
            while (await reader.ReadLineAsync() is {} line)
            {
                launcher.SendLine(line, error);
            }
        }
    }

    /// <summary>
    ///     The editor reports readiness on <paramref name="readyPipe" />; launcher settings follow it via the home
    ///     variable.
    /// </summary>
    private static ProcessStartInfo EditorInfo(EditorConsoleRequest request, string readyPipe)
    {
        ProcessStartInfo info = new( request.Executable )
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = !OperatingSystem.IsWindows(),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.Environment[Constants.Storage.HomeEnvironment] = request.Home;
        info.Environment["PROWL_EDITOR_READY_PIPE"] = readyPipe;
        foreach (string argument in request.Arguments)
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }

    /// <summary>Strips ANSI escapes and control characters, and truncates very long lines.</summary>
    internal static string CleanOutput(string text)
    {
        text = Regex.Replace(text, @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))", "");
        text = new string(text.Where(c => !char.IsControl(c) || c == '\t').ToArray());
        return text.Length > MaxLineLength ? text[..MaxLineLength] + "…" : text;
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

    /// <summary>Fallback readiness for editors that never signal: a main window that answers messages.</summary>
    private static bool HasResponsiveWindow(Process process)
    {
        const uint SmtoBlockAbortIfHung = 0x0002 | 0x0001; // SMTO_ABORTIFHUNG | SMTO_BLOCK
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        try
        {
            process.Refresh();
            nint window = process.MainWindowHandle;
            return window != 0 && SendMessageTimeout(window, 0, 0, 0, SmtoBlockAbortIfHung, 100, out _) != 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nuint result);

    /// <summary>The pipe back to the launcher. Sends stop silently once the launcher disconnects.</summary>
    private sealed class Connection(Stream pipe) : IDisposable
    {
        private readonly object _lock = new();
        private readonly StreamWriter _output = new( pipe, new UTF8Encoding(false), 1024, true )
        {
            AutoFlush = true
        };
        private bool _connected = true;

        public void Dispose()
        {
            try
            {
                _output.Dispose();
            }
            catch (IOException)
            {
            }
        }

        public void SendLine(string text, bool error)
        {
            text = CleanOutput(text);
            if (text.Length > 0)
            {
                Send(new EditorConsoleEvent(Line: new EditorConsoleLine(DateTimeOffset.UtcNow, text, error)));
            }
        }

        public void Send(EditorConsoleEvent entry)
        {
            lock (_lock)
            {
                if (!_connected)
                {
                    return;
                }
                try
                {
                    _output.WriteLine(JsonSerializer.Serialize(entry));
                }
                catch (Exception error) when (error is IOException or ObjectDisposedException)
                {
                    _connected = false;
                }
            }
        }
    }

    /// <summary>The editor updates Console.Title; give it an attached console without showing a terminal.</summary>
    private sealed class HiddenConsole : IDisposable
    {
        private const int SwHide = 0;
        private readonly bool _allocated;

        internal HiddenConsole()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            FreeConsole();
            _allocated = AllocConsole();
            if (_allocated)
            {
                ShowWindow(GetConsoleWindow(), SwHide);
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
}
