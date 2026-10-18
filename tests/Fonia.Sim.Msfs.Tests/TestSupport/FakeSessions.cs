namespace Fonia.Sim.Msfs.Tests.TestSupport;

/// <summary>A scripted <see cref="ISimConnectSession"/>: the test decides how opening, subscribing and probing behave.</summary>
internal sealed class FakeSession : ISimConnectSession
{
    private readonly Queue<SessionHealth> _probeResults = new();

    /// <summary>What <see cref="OpenAsync"/> does. Default: succeeds at once.</summary>
    public Func<CancellationToken, Task> OnOpen { get; set; } = _ => Task.CompletedTask;

    /// <summary>What <see cref="SubscribeAsync"/> does after remembering the sink. Default: succeeds.</summary>
    public Func<Task> OnSubscribe { get; set; } = () => Task.CompletedTask;

    /// <summary>The sink the adapter gave this session, so a test can play the simulator's data into it.</summary>
    public ISimConnectSink? Sink { get; private set; }

    public bool Disposed { get; private set; }

    public int ProbeCount { get; private set; }

    /// <summary>The answers for the next probes, in order; after they run out, the probe says <see cref="SessionHealth.Alive"/>.</summary>
    public void QueueProbes(params SessionHealth[] results)
    {
        foreach (var result in results)
        {
            _probeResults.Enqueue(result);
        }
    }

    public Task OpenAsync(CancellationToken ct) => OnOpen(ct);

    public async Task SubscribeAsync(ISimConnectSink sink, CancellationToken ct)
    {
        Sink = sink;
        await OnSubscribe();
    }

    public Task<SessionHealth> ProbeAsync(CancellationToken ct)
    {
        ProbeCount++;
        return Task.FromResult(_probeResults.Count > 0 ? _probeResults.Dequeue() : SessionHealth.Alive);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Plays one complete set of valid data into the sink: the simulation running and every group.</summary>
    public void PlayFlight(double latitude = -27.13246, string registration = "PR-FAB", double speedKnots = 79.4)
    {
        var sink = Sink ?? throw new InvalidOperationException("The adapter has not subscribed yet.");
        sink.OnSimRunning(true);
        sink.OnIdentity(new IdentityData(registration, "Cessna", "C25C", "Cessna CJ4"));
        sink.OnRadios(new RadioData(130_850_000, 124_850_000, 124_850_000, 124_850_000));
        sink.OnTransponder(new TransponderData(0x1200));
        sink.OnMotion(new MotionData(latitude, -52.66867, 5675, 3637, speedKnots, 21, 0));
    }
}

/// <summary>Hands the adapter the sessions a test scripted, and lets the test wait until one has been created.</summary>
internal sealed class FakeSessionFactory(Func<int, FakeSession> script) : ISimConnectSessionFactory
{
    private readonly object _gate = new();
    private readonly List<FakeSession> _created = [];

    public IReadOnlyList<FakeSession> Created
    {
        get
        {
            lock (_gate)
            {
                return [.. _created];
            }
        }
    }

    public ISimConnectSession Create()
    {
        lock (_gate)
        {
            var session = script(_created.Count);
            _created.Add(session);
            return session;
        }
    }

    /// <summary>Waits until the adapter has asked for at least <paramref name="count"/> sessions.</summary>
    public async Task WaitForSessionsAsync(int count, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        while (Created.Count < count)
        {
            await Task.Delay(2, timeout.Token);
        }
    }

    /// <summary>A session that opens and subscribes at once.</summary>
    public static FakeSession Healthy() => new();

    /// <summary>A session whose open fails because MSFS is not reachable.</summary>
    public static FakeSession Unavailable() => new()
    {
        OnOpen = _ => Task.FromException(new SimulatorUnavailableException("MSFS is not running.")),
    };
}
