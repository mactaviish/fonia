using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SimConnect.NET;
using SimConnect.NET.Events;
using SimConnect.NET.SimVar;

namespace Fonia.Sim.Msfs;

/// <summary>
/// An <see cref="ISimConnectSession"/> on top of SimConnect.NET. Everything that was learned from the
/// spike is applied here: handlers are attached before connecting (so the Open message cannot be missed),
/// subscriptions are created only after Open, the library's own auto-reconnect is off (it never noticed
/// MSFS closing), and a dead connection is told apart from a simulator that is just busy.
/// </summary>
internal sealed partial class SimConnectNetSession(
    ILogger<SimConnectNetSession> logger,
    Func<bool>? isSimulatorRunning = null) : ISimConnectSession
{
    private const string ApplicationName = "Fonia";
    private const uint SimEventId = 1;

    private static readonly TimeSpan OpenCheckInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<bool> _isSimulatorRunning = isSimulatorRunning ?? IsSimulatorProcessRunning;
    private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<ISimVarSubscription> _subscriptions = [];
    private SimConnectClient? _client;
    private ISimConnectSink? _sink;
    private int _faulted;
    private bool _disposed;

    public async Task OpenAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_client is not null)
        {
            throw new InvalidOperationException("The session is already open; create a new one.");
        }

        // Handlers first: the Open message can arrive right after ConnectAsync returns.
        var client = new SimConnectClient(ApplicationName) { AutoReconnectEnabled = false };
        client.RawMessageReceived += OnRawMessage;
        client.ErrorOccurred += OnError;
        _client = client;

        try
        {
            await client.ConnectAsync(cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            await DisposeClientAsync();
            throw;
        }
        catch (Exception ex)
        {
            await DisposeClientAsync();
            LogCannotConnect(ex.Message);
            throw new SimulatorUnavailableException("MSFS is not reachable.", ex);
        }

        LogConnected();

        try
        {
            await WaitForOpenAsync(ct);
        }
        catch
        {
            await DisposeClientAsync();
            throw;
        }
    }

    public async Task SubscribeAsync(ISimConnectSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var client = _client ?? throw new InvalidOperationException("Open the session before subscribing.");
        _sink = sink;

        client.SystemEventReceived += (_, e) =>
        {
            if (e.EventId == SimEventId)
            {
                Deliver("Sim", () => sink.OnSimRunning(e.Data == 1));
            }
        };

        await client.SubscribeToEventAsync("Sim", SimEventId, ct);

        Subscribe<MotionStruct>(client, "Motion", s => sink.OnMotion(s.ToData()));
        Subscribe<IdentityStruct>(client, "Identity", s => sink.OnIdentity(s.ToData()));
        Subscribe<RadiosStruct>(client, "Radios", s => sink.OnRadios(s.ToData()));
        SubscribeOptional<AirlineStruct>(client, "Airline", s => sink.OnAirline(s.ToData()));
        Subscribe<TransponderStruct>(client, "Transponder", s => sink.OnTransponder(s.ToData()));

        LogSubscribed();
    }

    public async Task<SessionHealth> ProbeAsync(CancellationToken ct)
    {
        var client = _client;
        if (client is null || _disposed)
        {
            return SessionHealth.Dead;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            // Any variable will do; MSFS answers it whenever a flight is running.
            await client.SimVars.GetAsync<double>("PLANE LATITUDE", "degrees", cancellationToken: timeout.Token);
            return SessionHealth.Alive;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return SessionHealth.NotResponding;
        }
        catch (Exception ex) when (SimConnectFailure.IsDeadConnection(ex))
        {
            LogProbeDead(ex.Message);
            return SessionHealth.Dead;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unknown failure: do not tear the session down for something we cannot tell apart from "busy".
            LogProbeUnknown(ex.GetType().Name, ex.Message);
            return SessionHealth.NotResponding;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var subscription in _subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception ex)
            {
                LogDisposeFailed(ex.Message);
            }
        }

        _subscriptions.Clear();
        await DisposeClientAsync();
    }

    private static bool IsSimulatorProcessRunning()
    {
        foreach (var name in new[] { "FlightSimulator", "FlightSimulator2024" })
        {
            var processes = Process.GetProcessesByName(name);

            try
            {
                if (processes.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }

    // The connection is accepted before MSFS is ready; the Open message comes later (seconds to minutes).
    // While waiting, notice if MSFS disappeared (crashed while starting): it would never send Open.
    private async Task WaitForOpenAsync(CancellationToken ct)
    {
        while (!_opened.Task.IsCompleted)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var finished = await Task.WhenAny(_opened.Task, Task.Delay(OpenCheckInterval, wait.Token));
            await wait.CancelAsync();
            ct.ThrowIfCancellationRequested();

            if (finished == _opened.Task)
            {
                break;
            }

            if (!_isSimulatorRunning())
            {
                throw new SimulatorUnavailableException("MSFS closed before it was ready.");
            }

            LogWaitingForOpen();
        }

        LogOpen();
    }

    private void Subscribe<T>(SimConnectClient client, string group, Action<T> deliver)
        where T : struct
    {
        var subscription = client.SimVars.Subscribe<T>(SimConnectPeriod.Second, value => Deliver(group, () => deliver(value)));
        _subscriptions.Add(subscription);

        _ = subscription.Completion.ContinueWith(
            task => Fault($"The {group} subscription failed: {task.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    // A group the app can do without: if the simulator refuses it, it is logged and the session goes on.
    private void SubscribeOptional<T>(SimConnectClient client, string group, Action<T> deliver)
        where T : struct
    {
        try
        {
            var subscription = client.SimVars.Subscribe<T>(SimConnectPeriod.Second, value => Deliver(group, () => deliver(value)));
            _subscriptions.Add(subscription);

            _ = subscription.Completion.ContinueWith(
                task => LogOptionalFailed(group, task.Exception?.GetBaseException().Message ?? string.Empty),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOptionalFailed(group, ex.Message);
        }
    }

    // Runs on the library's threads: whatever the sink throws must not reach them.
    private void Deliver(string group, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LogSinkFailed(group, ex);
        }
    }

    private void Fault(string reason)
    {
        if (Interlocked.Exchange(ref _faulted, 1) == 0)
        {
            LogFault(reason);
            Deliver("Fault", () => _sink?.OnFault(reason));
        }
    }

    private void OnRawMessage(object? sender, RawSimConnectMessageEventArgs e)
    {
        if (e.MessageId == SimConnectRecvId.Open)
        {
            _opened.TrySetResult();
        }
    }

    private void OnError(object? sender, SimConnectErrorEventArgs e)
    {
        LogLibraryError(e.Error, e.Context, e.Exception);

        // Seen when subscriptions were created before the server was ready: those groups never deliver.
        if (e.Error == SimConnectError.UnrecognizedId && _sink is not null)
        {
            Fault($"SimConnect rejected a request ({e.Error}): {e.Context}");
        }
    }

    // Disposing can hang or throw on a dead connection: never let it block recovery.
    private async Task DisposeClientAsync()
    {
        var client = _client;
        _client = null;

        if (client is null)
        {
            return;
        }

        var dispose = client.DisposeAsync().AsTask();
        var finished = await Task.WhenAny(dispose, Task.Delay(DisposeTimeout));

        if (finished != dispose)
        {
            LogDisposeAbandoned();
            _ = dispose.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return;
        }

        try
        {
            await dispose;
        }
        catch (Exception ex)
        {
            LogDisposeFailed(ex.Message);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Connected to SimConnect; waiting for MSFS to be ready.")]
    private partial void LogConnected();

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "MSFS is ready (Open message received).")]
    private partial void LogOpen();

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Cannot connect to SimConnect: {Reason}")]
    private partial void LogCannotConnect(string reason);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Still waiting for MSFS to finish starting.")]
    private partial void LogWaitingForOpen();

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Subscribed to the simulation state and the variable groups.")]
    private partial void LogSubscribed();

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "The connection is dead: {Reason}")]
    private partial void LogProbeDead(string reason);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "The connection probe failed with {ExceptionType}: {Reason}. Treating the simulator as busy.")]
    private partial void LogProbeUnknown(string exceptionType, string reason);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "The session cannot deliver data any more: {Reason}")]
    private partial void LogFault(string reason);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "The consumer of the {Group} data threw.")]
    private partial void LogSinkFailed(string group, Exception exception);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "SimConnect reported {Error} ({Context}).")]
    private partial void LogLibraryError(SimConnectError error, string context, Exception? exception);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning, Message = "Closing the SimConnect client failed: {Reason}")]
    private partial void LogDisposeFailed(string reason);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning, Message = "The optional {Group} group could not be read ({Reason}); the aircraft is called by its registration.")]
    private partial void LogOptionalFailed(string group, string reason);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning, Message = "Closing the SimConnect client did not finish in time; abandoning it.")]
    private partial void LogDisposeAbandoned();
}
