using System.Globalization;
using Fonia.Core.Sim;
using Fonia.Sim.Mock.Telemetry;

namespace Fonia.App;

/// <summary>
/// Runs the app: shows the state from the chosen source until it ends, the duration runs out or the user cancels,
/// optionally recording the session. Everything it needs comes in through the constructor, so tests drive it without a
/// console or a simulator.
/// </summary>
/// <param name="interactive">True on a terminal: the state line is rewritten in place instead of scrolling.</param>
/// <param name="maxLineWidth">Width of the terminal minus one, or 0 for unlimited. A line wider than that cannot be rewritten in place and scrolls instead.</param>
internal sealed class AppRunner(
    ISimSourceFactory sourceFactory,
    TextWriter output,
    TextWriter error,
    bool interactive,
    int maxLineWidth = 0,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _writeLock = new();
    private bool _lineOpen;
    private int _lineWidth;
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
            WriteState(StateFormatter.Format(state));
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

    private void WriteState(string line)
    {
        lock (_writeLock)
        {
            var fits = maxLineWidth <= 0 || line.Length <= maxLineWidth;

            if (interactive && fits)
            {
                // Overwrite the previous state line; pad so a shorter line leaves nothing behind.
                output.Write('\r');
                output.Write(line.PadRight(_lineWidth));
                _lineWidth = line.Length;
                _lineOpen = true;
            }
            else
            {
                EndLineLocked();
                output.WriteLine(line);
            }
        }
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
            _lineWidth = 0;
        }
    }
}
