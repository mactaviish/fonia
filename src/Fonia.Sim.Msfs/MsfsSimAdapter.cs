using Fonia.Core.Sim;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fonia.Sim.Msfs;

/// <summary>
/// <see cref="ISimAdapter"/> for Microsoft Flight Simulator 2020 over SimConnect.
/// </summary>
/// <remarks>
/// <para>
/// After <see cref="ConnectAsync"/> a background supervisor owns the connection. It keeps trying to reach MSFS
/// with no limit (the simulator can take minutes to start), waits for MSFS to be ready, reads the data, checks
/// the connection every few seconds and, when it is gone, throws the SimConnect client away and builds a new one
/// (the library never notices MSFS closing). Streams stay open and <see cref="LatestState"/> is kept through all of
/// it. See docs/decisions/simconnect.md for what each rule is based on.
/// </para>
/// <para>This adapter only reads; tuning the radios from the app arrives with the frequency handoff (M4).</para>
/// </remarks>
public sealed partial class MsfsSimAdapter : ISimAdapter
{
    private readonly ISimConnectSessionFactory _sessionFactory;
    private readonly ILogger<MsfsSimAdapter> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly MsfsAdapterOptions _options;
    private readonly AircraftStateBroadcaster _broadcaster = new();
    private readonly MsfsSnapshotAssembler _assembler;
    private readonly Lock _gate = new();

    private SimConnectionState _state = SimConnectionState.Disconnected;
    private CancellationTokenSource? _supervisorCts;
    private Task? _supervisor;
    private TaskCompletionSource _firstConnection = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    /// <param name="loggerFactory">Where to log; defaults to discarding.</param>
    /// <param name="timeProvider">The clock used for the pauses; defaults to the system clock.</param>
    /// <param name="options">Retry and probe timing; defaults to the validated values.</param>
    public MsfsSimAdapter(ILoggerFactory? loggerFactory = null, TimeProvider? timeProvider = null, MsfsAdapterOptions? options = null)
        : this(
            new SimConnectNetSessionFactory(loggerFactory ?? NullLoggerFactory.Instance),
            (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<MsfsSimAdapter>(),
            timeProvider ?? TimeProvider.System,
            options ?? new MsfsAdapterOptions())
    {
    }

    internal MsfsSimAdapter(
        ISimConnectSessionFactory sessionFactory,
        ILogger<MsfsSimAdapter> logger,
        TimeProvider timeProvider,
        MsfsAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.InitialRetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxRetryDelay, options.InitialRetryDelay);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ProbeInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DeadProbesBeforeReconnect, 1);

        _sessionFactory = sessionFactory;
        _logger = logger;
        _timeProvider = timeProvider;
        _options = options;
        _assembler = new MsfsSnapshotAssembler(timeProvider);
    }

    public event EventHandler<SimConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public SimConnectionState ConnectionState
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public AircraftState? LatestState => _broadcaster.Latest;

    public async Task ConnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        List<SimConnectionStateChangedEventArgs> changes = [];
        Task firstConnection;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_state == SimConnectionState.Disconnected)
            {
                _firstConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _supervisorCts = new CancellationTokenSource();
                ChangeState(SimConnectionState.Connecting, changes);

                var owner = _supervisorCts;
                // Deliberately not tied to a token: the supervisor must always run to the end to dispose its session.
                _supervisor = Task.Run(() => SuperviseAsync(owner), CancellationToken.None);
            }

            // Already connected or reconnecting: nothing to start, and the first connection is long done.
            firstConnection = _firstConnection.Task;
        }

        Raise(changes);

        try
        {
            await firstConnection.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            await StopAsync();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await StopAsync();
    }

    public IAsyncEnumerable<AircraftState> StreamAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // The subscription starts at this call, so nothing published afterwards is missed.
            return _broadcaster.Subscribe(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await StopAsync();
    }

    // Ends the supervisor, the streams and the connection. Safe to call at any time and more than once.
    private async Task StopAsync()
    {
        List<SimConnectionStateChangedEventArgs> changes = [];
        CancellationTokenSource? cts;
        Task? supervisor;

        lock (_gate)
        {
            cts = _supervisorCts;
            supervisor = _supervisor;
            _supervisorCts = null;
            _supervisor = null;

            if (_state != SimConnectionState.Disconnected)
            {
                ChangeState(SimConnectionState.Disconnected, changes);
            }

            _firstConnection.TrySetCanceled();
            _broadcaster.CompleteSubscriptions();
            _assembler.Reset();
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
        }

        if (supervisor is not null)
        {
            // The supervisor disposes its session on the way out; it never throws.
            await supervisor;
        }

        cts?.Dispose();
        Raise(changes);
    }

    private async Task SuperviseAsync(CancellationTokenSource owner)
    {
        var token = owner.Token;
        var retryDelay = _options.InitialRetryDelay;

        while (!token.IsCancellationRequested)
        {
            ISimConnectSession? session = null;
            SupervisedSink? sink = null;

            try
            {
                _assembler.Reset();
                session = _sessionFactory.Create();
                sink = new SupervisedSink(this);

                await session.OpenAsync(token);
                await session.SubscribeAsync(sink, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await CloseAsync(session, sink);
                return;
            }
            catch (SimulatorUnavailableException ex)
            {
                LogNotReachable(ex.Message, retryDelay);
                await CloseAsync(session, sink);
                retryDelay = await PauseAsync(retryDelay, token);
                continue;
            }
            catch (Exception ex)
            {
                LogSetupFailed(ex, retryDelay);
                await CloseAsync(session, sink);
                retryDelay = await PauseAsync(retryDelay, token);
                continue;
            }

            if (!SetStateIfOwner(owner, SimConnectionState.Connected))
            {
                await CloseAsync(session, sink);
                return;
            }

            LogConnected();
            retryDelay = _options.InitialRetryDelay;
            _firstConnection.TrySetResult();

            var lostBecause = await MonitorAsync(session, sink, token);
            await CloseAsync(session, sink);

            if (token.IsCancellationRequested)
            {
                return;
            }

            LogLost(lostBecause);
            SetStateIfOwner(owner, SimConnectionState.Connecting);

            // Try again at once: if MSFS is simply gone, the first attempt fails in milliseconds and the
            // pauses start from the shortest.
        }
    }

    // Returns why the connection has to be rebuilt, or null when the adapter is being stopped.
    private async Task<string?> MonitorAsync(ISimConnectSession session, SupervisedSink sink, CancellationToken token)
    {
        var deadInARow = 0;

        while (!token.IsCancellationRequested)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            var finished = await Task.WhenAny(Task.Delay(_options.ProbeInterval, _timeProvider, wait.Token), sink.Fault);
            await wait.CancelAsync();

            if (token.IsCancellationRequested)
            {
                return null;
            }

            if (finished == sink.Fault)
            {
                return await sink.Fault;
            }

            SessionHealth health;

            try
            {
                health = await session.ProbeAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                // A probe that blows up says nothing reliable about the connection: treat it as "busy".
                LogProbeThrew(ex);
                health = SessionHealth.NotResponding;
            }

            // Only dead probes in a row count. MSFS also stops answering in menus and while loading a flight.
            deadInARow = health == SessionHealth.Dead ? deadInARow + 1 : 0;

            if (deadInARow >= _options.DeadProbesBeforeReconnect)
            {
                return $"{deadInARow} probes in a row found the connection dead";
            }
        }

        return null;
    }

    // Waits, then returns the next (longer, capped) pause.
    private async Task<TimeSpan> PauseAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, token);
        }
        catch (OperationCanceledException)
        {
            // Stopping: the loop ends on the next check.
        }

        var doubled = TimeSpan.FromTicks(delay.Ticks * 2);
        return doubled > _options.MaxRetryDelay ? _options.MaxRetryDelay : doubled;
    }

    private async Task CloseAsync(ISimConnectSession? session, SupervisedSink? sink)
    {
        // Anything this session still delivers from now on belongs to a connection that no longer exists.
        sink?.Close();

        if (session is null)
        {
            return;
        }

        try
        {
            await session.DisposeAsync();
        }
        catch (Exception ex)
        {
            LogDisposeFailed(ex);
        }
    }

    // Only the supervisor that owns the current run may change the state; a stale one (after a disconnect) must not.
    private bool SetStateIfOwner(CancellationTokenSource owner, SimConnectionState next)
    {
        List<SimConnectionStateChangedEventArgs> changes = [];

        lock (_gate)
        {
            if (!ReferenceEquals(_supervisorCts, owner))
            {
                return false;
            }

            if (_state != next)
            {
                ChangeState(next, changes);
            }
        }

        Raise(changes);
        return true;
    }

    // Must be called with _gate held.
    private void ChangeState(SimConnectionState next, List<SimConnectionStateChangedEventArgs> changes)
    {
        changes.Add(new SimConnectionStateChangedEventArgs(_state, next));
        _state = next;
    }

    // Handlers run outside the lock so they can call back into the adapter from any thread.
    private void Raise(List<SimConnectionStateChangedEventArgs> changes)
    {
        foreach (var change in changes)
        {
            ConnectionStateChanged?.Invoke(this, change);
        }
    }

    private void Publish(AircraftState? state)
    {
        if (state is not null)
        {
            _broadcaster.Publish(state);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Connected to MSFS.")]
    private partial void LogConnected();

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "MSFS is not reachable yet ({Reason}); trying again in {Delay}.")]
    private partial void LogNotReachable(string reason, TimeSpan delay);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Setting up the SimConnect session failed; trying again in {Delay}.")]
    private partial void LogSetupFailed(Exception exception, TimeSpan delay);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "The connection to MSFS was lost ({Reason}); reconnecting.")]
    private partial void LogLost(string? reason);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "The connection probe threw; treating MSFS as busy.")]
    private partial void LogProbeThrew(Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Closing the SimConnect session failed.")]
    private partial void LogDisposeFailed(Exception exception);

    /// <summary>Feeds one session's data into the shared assembler, until that session is closed.</summary>
    private sealed class SupervisedSink(MsfsSimAdapter owner) : ISimConnectSink
    {
        private readonly TaskCompletionSource<string> _fault = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _closed;

        public Task<string> Fault => _fault.Task;

        public void Close() => _closed = true;

        public void OnSimRunning(bool running)
        {
            if (!_closed)
            {
                owner._assembler.OnSimRunning(running);
            }
        }

        public void OnMotion(MotionData data)
        {
            if (!_closed)
            {
                owner.Publish(owner._assembler.OnMotion(data));
            }
        }

        public void OnIdentity(IdentityData data)
        {
            if (!_closed)
            {
                owner.Publish(owner._assembler.OnIdentity(data));
            }
        }

        public void OnAirline(AirlineData data)
        {
            if (!_closed)
            {
                owner.Publish(owner._assembler.OnAirline(data));
            }
        }

        public void OnRadios(RadioData data)
        {
            if (!_closed)
            {
                owner.Publish(owner._assembler.OnRadios(data));
            }
        }

        public void OnTransponder(TransponderData data)
        {
            if (!_closed)
            {
                owner.Publish(owner._assembler.OnTransponder(data));
            }
        }

        public void OnFault(string reason)
        {
            if (!_closed)
            {
                _fault.TrySetResult(reason);
            }
        }
    }
}
