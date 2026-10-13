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
/// Asked on every update how wide the terminal is, so the state is laid out over as many rows as that width needs, and
/// used to rewrite those rows in place, erasing the old ones even when a resize has made the terminal wrap them again.
/// <see langword="null"/> (or unknown values) means one row and no cursor control.
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
    private List<string> _block = [];  // the rows of the state as written to the screen (padding included); empty when none is on screen
    private int _blockWrap;                // the width at which the terminal wrapped the block when it was written
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

            List<string> rows = terminal is null ? [StateFormatter.Format(state)] : [.. StateFormatter.FormatRows(state, terminal.Columns)];
            Rewrite(rows);
        }
    }

    // Draws the state at the place where the previous one was, so it never adds lines: the block is one row in a wide
    // window and a few in a narrow one, and the same rows are redrawn on every update. The cursor always rests at the
    // end of the block, which is how its place is found again. Must be called with _writeLock held.
    private void Rewrite(List<string> rows)
    {
        var wrap = terminal?.WrapColumns ?? 0;

        if (_block.Count == 0)
        {
            Draw(rows, wrap);
            return;
        }

        if (terminal is null)
        {
            // No cursor control: a single row, rewritten from the start of its line.
            output.Write('\r');
            Draw([rows[0].PadRight(_block[0].Length)], wrap);
            return;
        }

        // How many screen rows the old block covers now: a window that shrank since it was written has made the
        // terminal wrap its rows again, and the cursor sits at the end of the last of them.
        var covered = ScreenRows(_block, wrap);
        var top = Math.Max(0, terminal.CursorTop - (covered - 1));

        if (wrap == _blockWrap && rows.Count == _block.Count && covered == _block.Count)
        {
            // The usual case: the same rows as before. Overwrite them, padded so shorter text leaves nothing behind.
            var padded = rows.Select((row, i) => row.PadRight(_block[i].Length)).ToList();

            for (var i = 0; i < padded.Count; i++)
            {
                terminal.SetCursor(0, top + i);
                output.Write(padded[i]);
            }

            _block = padded;
            return;
        }

        // The layout changed (the window was resized): blank every screen row the old block covers and draw the new
        // block from the first of them, so no piece of the old one is left behind.
        var blank = new string(' ', wrap > 0 ? wrap : _block.Max(row => row.Length));

        for (var i = 0; i < covered; i++)
        {
            terminal.SetCursor(0, top + i);
            output.Write(blank);
        }

        terminal.SetCursor(0, top);
        Draw(rows, wrap);
    }

    private void Draw(List<string> rows, int wrap)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            output.Write(rows[i]);

            if (i < rows.Count - 1)
            {
                output.WriteLine();
            }
        }

        _block = rows;
        _blockWrap = wrap;
    }

    // Screen rows taken by rows of text when the terminal wraps at wrap columns (one per row when it does not say).
    private static int ScreenRows(List<string> rows, int wrap)
    {
        return wrap <= 0 ? rows.Count : rows.Sum(row => Math.Max(1, (row.Length + wrap - 1) / wrap));
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
        if (_block.Count > 0)
        {
            output.WriteLine();
            _block = [];
        }
    }
}
