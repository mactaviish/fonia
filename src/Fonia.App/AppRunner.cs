using System.Globalization;
using Fonia.Core.Sim;
using Fonia.Sim.Mock.Telemetry;

namespace Fonia.App;

/// <summary>
/// Runs the app: shows the state from the chosen source until it ends, the duration runs out or the user cancels,
/// optionally recording the session. Everything it needs comes in through the constructor, so tests drive it without a
/// console or a simulator.
/// </summary>
/// <param name="interactive">True on a terminal: the state is one line rewritten in place. Otherwise (output redirected to a file) every snapshot is its own line.</param>
/// <param name="terminal">
/// Asked on every update how wide the terminal is, so the state line is fitted to it and never wraps, and used to
/// clean up when a shrinking window has already made the terminal wrap the previous line onto several rows.
/// <see langword="null"/> (or unknown values) means no limit and no clean-up.
/// </param>
internal sealed class AppRunner(
    ISimSourceFactory sourceFactory,
    TextWriter output,
    TextWriter error,
    bool interactive,
    ITerminal? terminal = null,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _writeLock = new();
    private bool _lineOpen;
    private int _writtenLength; // characters of the state line on screen, padding included
    private bool _everConnected;

    /// <summary>Returns the exit code: 0 normal, 1 failure while running, 2 wrong usage.</summary>
    public async Task<int> RunAsync(AppOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Source == SimSourceKind.Replay && !File.Exists(options.ReplayPath))
        {
            error.WriteLine($"fonia: arquivo de reprodução não encontrado: {options.ReplayPath}");
            return 2;
        }

        using var timeout = options.Duration is { } duration ? new CancellationTokenSource(duration, _time) : new CancellationTokenSource();
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        FileStream? replayFile = null;
        FileStream? recordFile = null;
        SimSource? source = null;
        Task? display = null;
        Task? recording = null;
        var exitCode = 0;

        try
        {
            replayFile = options.Source == SimSourceKind.Replay ? File.OpenRead(options.ReplayPath!) : null;
            recordFile = options.RecordPath is { } path ? new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read) : null;
            source = sourceFactory.Create(options, replayFile);

            // Subscribe before connecting, so no snapshot published during the connection is missed.
            display = DisplayAsync(source.Adapter.StreamAsync(run.Token));

            if (recordFile is not null)
            {
                recording = new TelemetryRecorder(source.Adapter, recordFile, leaveOpen: false).RunAsync(run.Token);
            }

            source.Adapter.ConnectionStateChanged += OnConnectionStateChanged;

            WriteLine($"Fonia - {source.Description}. Ctrl+C para sair.");

            await source.Adapter.ConnectAsync(run.Token);

            if (source.Drive is { } drive)
            {
                await drive(run.Token);

                if (source.EndsWhenDriveCompletes)
                {
                    // Let the display show what is still buffered before announcing the end.
                    await run.CancelAsync();
                    await FinishAsync(display);
                    WriteStatus("Reprodução concluída.");
                }
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, run.Token);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // The normal way out: Ctrl+C, the duration ran out, or a replay reached its end.
        }
        catch (TelemetryFormatException ex)
        {
            error.WriteLine($"fonia: {ex.Message}");
            exitCode = 1;
        }
        catch (IOException ex)
        {
            error.WriteLine($"fonia: erro de arquivo: {ex.Message}");
            exitCode = 1;
        }
        finally
        {
            await run.CancelAsync();

            if (source is not null)
            {
                await DisposeQuietlyAsync(source.Adapter);
            }

            await FinishAsync(display);
            await FinishAsync(recording);

            recordFile?.Dispose();
            replayFile?.Dispose();
            EndLine();
        }

        return exitCode;
    }

    private async Task DisplayAsync(IAsyncEnumerable<AircraftState> states)
    {
        await foreach (var state in states)
        {
            WriteState(state);
        }
    }

    private void OnConnectionStateChanged(object? sender, SimConnectionStateChangedEventArgs e)
    {
        string? message = (e.Previous, e.Current) switch
        {
            (SimConnectionState.Disconnected, SimConnectionState.Connecting) => "Conectando ao simulador...",
            (SimConnectionState.Connecting, SimConnectionState.Connected) when _everConnected => "Reconectado.",
            (SimConnectionState.Connecting, SimConnectionState.Connected) => "Conectado. Os dados aparecem quando um voo estiver carregado.",
            (SimConnectionState.Connected, SimConnectionState.Connecting) => "Conexão com o simulador perdida; tentando reconectar...",
            (_, SimConnectionState.Disconnected) => "Desconectado.",
            _ => null,
        };

        if (e.Current == SimConnectionState.Connected)
        {
            _everConnected = true;
        }

        if (message is not null)
        {
            WriteStatus(message);
        }
    }

    private static async Task DisposeQuietlyAsync(ISimAdapter adapter)
    {
        try
        {
            await adapter.DisposeAsync();
        }
        catch (Exception)
        {
            // Closing down: nothing useful to do with a failure here.
        }
    }

    // A task that ended because the run was cancelled is not a problem.
    private static async Task FinishAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WriteStatus(string message)
    {
        var time = _time.GetLocalNow().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        WriteLine($"[{time}] {message}");
    }

    // A normal line of output; first ends the state line that is being rewritten, if any.
    private void WriteLine(string text)
    {
        lock (_writeLock)
        {
            EndLineLocked();
            output.WriteLine(text);
        }
    }

    private void WriteState(AircraftState state)
    {
        lock (_writeLock)
        {
            if (!interactive)
            {
                output.WriteLine(StateFormatter.Format(state));
                return;
            }

            var columns = terminal?.Columns ?? 0;
            var line = StateFormatter.Format(state, columns);

            if (!EraseWrappedLine())
            {
                output.Write('\r');
            }

            // Padding erases what a longer previous line left behind, but never goes past the window.
            var padTo = columns > 0 ? Math.Min(_writtenLength, columns) : _writtenLength;
            var text = line.PadRight(padTo);

            output.Write(text);
            _writtenLength = text.Length;
            _lineOpen = true;
        }
    }

    // When the window has been made narrower than the line already on screen, the terminal re-wraps that line onto
    // several rows and leaves the cursor at the end of the last one. A carriage return would only go back to the start
    // of that last row, so the update would be drawn below the rest, as if it were a new line. Instead, go up to the
    // first row, blank every row the old line covers, and let the caller draw from there. Returns false when there was
    // nothing wrapped (the usual case), so the caller does a plain carriage return.
    // Must be called with _writeLock held.
    private bool EraseWrappedLine()
    {
        var wrap = terminal?.WrapColumns ?? 0;

        if (terminal is null || !_lineOpen || wrap <= 0 || _writtenLength <= wrap)
        {
            return false;
        }

        var rows = (_writtenLength + wrap - 1) / wrap;
        var top = Math.Max(0, terminal.CursorTop - (rows - 1));

        terminal.SetCursor(0, top);
        output.Write(new string(' ', _writtenLength));
        terminal.SetCursor(0, top);
        _writtenLength = 0;
        return true;
    }

    private void EndLine()
    {
        lock (_writeLock)
        {
            EndLineLocked();
        }
    }

    // Must be called with _writeLock held.
    private void EndLineLocked()
    {
        if (_lineOpen)
        {
            output.WriteLine();
            _lineOpen = false;
            _writtenLength = 0;
        }
    }
}
