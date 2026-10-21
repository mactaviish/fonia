using Fonia.Core.Sim;
using Microsoft.Extensions.Logging.Abstractions;
using SimConnect.NET;

namespace Fonia.Sim.Msfs.Tests;

/// <summary>
/// Runs the real SimConnect session against a running MSFS 2020 with a flight loaded. Read-only: it never
/// changes anything in the simulator. Skipped unless <c>FONIA_LIVE_MSFS=1</c>, so CI and machines without
/// the simulator just report them as skipped.
/// <code>
/// $env:FONIA_LIVE_MSFS = "1"; dotnet test --filter-class "*LiveMsfsSessionTests"
/// </code>
/// </summary>
[Trait("Category", "Live")]
public class LiveMsfsSessionTests
{
    private static readonly TimeSpan FlightLoadTimeout = TimeSpan.FromSeconds(90);

    private const string LiveSkipReason = "Needs a running MSFS 2020 with a flight loaded. Set FONIA_LIVE_MSFS=1 to run.";

    public static bool LiveEnabled => Environment.GetEnvironmentVariable("FONIA_LIVE_MSFS") == "1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SimConnectNetSession NewSession() => new(NullLogger<SimConnectNetSession>.Instance);

    /// <summary>Feeds the groups into the real assembler, as the adapter will, and remembers what it publishes.</summary>
    private sealed class AssemblingSink : ISimConnectSink
    {
        private readonly MsfsSnapshotAssembler _assembler = new(TimeProvider.System);
        private readonly TaskCompletionSource<AircraftState> _firstState = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _fault = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AircraftState> FirstState => _firstState.Task;

        public Task<string> Fault => _fault.Task;

        public void OnSimRunning(bool running) => _assembler.OnSimRunning(running);

        public void OnMotion(MotionData data) => Publish(_assembler.OnMotion(data));

        public void OnIdentity(IdentityData data) => Publish(_assembler.OnIdentity(data));

        public void OnAirline(AirlineData data) => Publish(_assembler.OnAirline(data));

        public void OnRadios(RadioData data) => Publish(_assembler.OnRadios(data));

        public void OnTransponder(TransponderData data) => Publish(_assembler.OnTransponder(data));

        public void OnFault(string reason) => _fault.TrySetResult(reason);

        private void Publish(AircraftState? state)
        {
            if (state is not null)
            {
                _firstState.TrySetResult(state);
            }
        }
    }

    private static async Task<AircraftState> OpenAndReadFirstStateAsync(SimConnectNetSession session, AssemblingSink sink)
    {
        await session.OpenAsync(Ct).WaitAsync(TimeSpan.FromMinutes(2), Ct);
        await session.SubscribeAsync(sink, Ct);

        var first = await Task.WhenAny(sink.FirstState, sink.Fault, Task.Delay(FlightLoadTimeout, Ct));

        if (first == sink.Fault)
        {
            throw new InvalidOperationException($"The session reported a fault: {await sink.Fault}");
        }

        if (first != sink.FirstState)
        {
            Assert.Skip($"No valid snapshot within {FlightLoadTimeout.TotalSeconds:F0} s: is a flight loaded in MSFS (not just the main menu)?");
        }

        return await sink.FirstState;
    }

    [Fact(Skip = LiveSkipReason, SkipUnless = nameof(LiveEnabled), SkipType = typeof(LiveMsfsSessionTests))]
    public async Task The_real_session_delivers_a_valid_snapshot_and_answers_probes()
    {
        await using var session = NewSession();
        var sink = new AssemblingSink();

        var state = await OpenAndReadFirstStateAsync(session, sink);

        state.LatitudeDegrees.ShouldBeInRange(-90, 90);
        state.LongitudeDegrees.ShouldBeInRange(-180, 180);
        (Math.Abs(state.LatitudeDegrees) >= 0.05 || Math.Abs(state.LongitudeDegrees) >= 0.05).ShouldBeTrue("the loading placeholder position was published");
        state.Registration.ShouldNotBeNullOrWhiteSpace();
        state.AircraftType.ShouldNotBeNullOrWhiteSpace();
        state.TransponderCode.ShouldBeInRange(0, 7777);
        state.Com1.ShouldNotBeNull();

        (await session.ProbeAsync(Ct)).ShouldBe(SessionHealth.Alive);
    }

    [Fact(Skip = LiveSkipReason, SkipUnless = nameof(LiveEnabled), SkipType = typeof(LiveMsfsSessionTests))]
    public async Task Radio_frequencies_from_the_session_match_an_independent_scalar_read()
    {
        // The spike found COM1 wrong through the struct path when read in MHz. Guard the production
        // definitions against the same kind of silent mismatch.
        await using var session = NewSession();
        var state = await OpenAndReadFirstStateAsync(session, new AssemblingSink());

        await using var reference = new SimConnectClient("Fonia live test reference");
        await reference.ConnectAsync(cancellationToken: Ct);

        foreach (var (variable, actual) in new[]
        {
            ("COM ACTIVE FREQUENCY:1", state.Com1?.Active),
            ("COM STANDBY FREQUENCY:1", state.Com1?.Standby),
            ("COM ACTIVE FREQUENCY:2", state.Com2?.Active),
            ("COM STANDBY FREQUENCY:2", state.Com2?.Standby),
        })
        {
            var hz = await reference.SimVars.GetAsync<double>(variable, "Hz", cancellationToken: Ct);
            var expected = MsfsConversions.ToFrequency(hz);

            actual.ShouldBe(expected, $"{variable}: the session read {actual}, an independent read says {expected} ({hz} Hz)");
        }
    }

    [Fact(Skip = LiveSkipReason, SkipUnless = nameof(LiveEnabled), SkipType = typeof(LiveMsfsSessionTests))]
    public async Task A_new_session_works_after_the_previous_one_was_disposed()
    {
        // The reconnection plan throws the old client away and builds a new one in the same process.
        var firstSink = new AssemblingSink();
        await using (var first = NewSession())
        {
            await OpenAndReadFirstStateAsync(first, firstSink);
        }

        var secondSink = new AssemblingSink();
        await using var second = NewSession();
        var state = await OpenAndReadFirstStateAsync(second, secondSink);

        state.Registration.ShouldNotBeNullOrWhiteSpace();
        (await second.ProbeAsync(Ct)).ShouldBe(SessionHealth.Alive);
    }

    [Fact(Skip = LiveSkipReason, SkipUnless = nameof(LiveEnabled), SkipType = typeof(LiveMsfsSessionTests))]
    public async Task Two_sessions_can_be_open_at_the_same_time()
    {
        // Not a requirement of the adapter, but it shows that disposing one session does not disturb another.
        await using var a = NewSession();
        await using var b = NewSession();
        var sinkA = new AssemblingSink();
        var sinkB = new AssemblingSink();

        await OpenAndReadFirstStateAsync(a, sinkA);
        await OpenAndReadFirstStateAsync(b, sinkB);
        await a.DisposeAsync();

        (await b.ProbeAsync(Ct)).ShouldBe(SessionHealth.Alive);
    }
}
