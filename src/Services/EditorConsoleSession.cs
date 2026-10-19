using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Prowl.Launcher;

/// <summary>
///     The launcher's view of one collector. Events are received on a background task; the UI thread calls
///     <see cref="Refresh" /> to copy the latest state into <see cref="State" />, <see cref="Lines" /> and
///     <see cref="Problems" />.
/// </summary>
internal sealed class EditorConsoleSession : IDisposable
{
    private const int MaxRetainedLines = 1000;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private readonly Process _collector;

    // Written by the receive task under _gate, published to the UI by Refresh.
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Queue<EditorConsoleLine> _lines = new();

    private readonly NamedPipeServerStream _pipe;
    private readonly Queue<EditorConsoleLine> _problems = new();
    private int _observedRevision = -1;
    private int _revision;
    private EditorConsoleState _state = new(DateTimeOffset.UtcNow);

    internal EditorConsoleSession(NamedPipeServerStream pipe, EditorConsoleRequest request, Process collector)
    {
        _pipe = pipe;
        Request = request;
        _collector = collector;
        Completion = Task.Run(ReceiveAsync);
    }

    internal string Id
    {
        get;
    } = Guid.NewGuid().ToString("N");

    internal EditorConsoleRequest Request
    {
        get;
    }

    internal Task Completion
    {
        get;
    }

    internal EditorConsoleState State
    {
        get;
        private set;
    } = new(DateTimeOffset.UtcNow);

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

    internal double Elapsed =>
        Math.Max(0, ((State.Ready ?? State.Ended ?? DateTimeOffset.UtcNow) - State.Started).TotalSeconds);

    public void Dispose()
    {
        _lifetime.Cancel();
        _pipe.Dispose();
        _collector.Dispose();
    }

    private async Task ReceiveAsync()
    {
        try
        {
            await _pipe.WaitForConnectionAsync(_lifetime.Token).WaitAsync(ConnectTimeout, _lifetime.Token);
            using StreamWriter writer = new(_pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(Request));
            using StreamReader reader = new(_pipe, Encoding.UTF8, false, 1024, true);
            while (await reader.ReadLineAsync(_lifetime.Token) is { } json)
            {
                Apply(JsonSerializer.Deserialize<EditorConsoleEvent>(json));
            }

            if (_state.Ended == null)
            {
                End("The editor log collector disconnected.");
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException
                                          or JsonException or TimeoutException)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                End(error.Message);
            }
        }
        finally
        {
            _pipe.Dispose();
        }
    }

    private void Apply(EditorConsoleEvent? entry)
    {
        lock (_gate)
        {
            _revision++;
            if (entry?.State is { } state)
            {
                _state = state;
            }

            if (entry?.Line is { } line)
            {
                Enqueue(_lines, line);
                if (line.IsProblem)
                {
                    Enqueue(_problems, line);
                }
            }
        }
    }

    private static void Enqueue(Queue<EditorConsoleLine> queue, EditorConsoleLine line)
    {
        queue.Enqueue(line);
        if (queue.Count > MaxRetainedLines)
        {
            queue.Dequeue();
        }
    }

    private void End(string error)
    {
        lock (_gate)
        {
            if (_state.Ended != null)
            {
                return;
            }

            _state = _state with { Ended = DateTimeOffset.UtcNow, Error = error };
            _revision++;
        }
    }

    /// <summary>Publishes received events to the UI-facing properties. Cheap when nothing changed.</summary>
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

    /// <summary>Waits until the collector has started the editor, throwing if it could not.</summary>
    internal async Task WaitForStartAsync(CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(ConnectTimeout);
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
}
