using Fonia.Core.Sim;
using Fonia.Sim.Mock;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.App.Tests;

public class SimSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class DelegateFactory(Func<SimSource> create) : ISimSourceFactory
    {
        public SimSource Create(AppOptions options, Stream? replay) => create();
    }

    private static SimSession MockSession(Action<string>? progress = null, TimeSpan? timeout = null) =>
        new(new SimSourceFactory(NullLoggerFactory.Instance, TimeProvider.System), TimeProvider.System, timeout ?? TimeSpan.FromSeconds(30), progress ?? (_ => { }));

    [Fact]
    public async Task It_reads_the_registration_of_the_first_state()
    {
        await using var session = MockSession();

        var registration = await session.ReadCallsignAsync(new AppOptions(Source: SimSourceKind.Mock, AtcAirport: "SBCT"), Ct);

        registration.ShouldBe("PR-ABC");
    }

    [Fact]
    public async Task It_keeps_the_connection_so_that_the_radio_can_be_read_afterwards()
    {
        await using var session = MockSession();
        session.Com1.ShouldBeNull("nothing has been read yet");

        await session.ReadCallsignAsync(new AppOptions(Source: SimSourceKind.Mock, AtcAirport: "SBCT"), Ct);

        session.Com1.ShouldBe(Frequency.FromMhz(121.9));
    }

    [Fact]
    public async Task It_follows_the_radio_when_the_pilot_tunes_it()
    {
        var sim = new MockSimAdapter(TimeProvider.System);
        sim.SetState(MockAircraft.TaxiSequence(1)[0]);
        await using var session = new SimSession(
            new DelegateFactory(() => new SimSource(sim, Drive: null, EndsWhenDriveCompletes: false, Description: "mock")),
            TimeProvider.System,
            TimeSpan.FromSeconds(30),
            _ => { });
        await session.ReadCallsignAsync(new AppOptions(AtcAirport: "SBCT"), Ct);

        sim.SetState(sim.LatestState! with { Com1 = new RadioState(Frequency.FromMhz(118.55), Frequency.FromMhz(121.9)) });
        await WaitForAsync(() => session.Com1 == Frequency.FromMhz(118.55));

        session.Com1.ShouldBe(Frequency.FromMhz(118.55));
    }

    [Fact]
    public async Task It_gives_up_with_null_when_no_state_comes_in_time()
    {
        var time = new FakeTimeProvider();
        var silent = new MockSimAdapter(time);
        await using var session = new SimSession(
            new DelegateFactory(() => new SimSource(silent, Drive: null, EndsWhenDriveCompletes: false, Description: "silent")),
            time,
            TimeSpan.FromSeconds(10),
            _ => { });

        var reading = session.ReadCallsignAsync(new AppOptions(AtcAirport: "SBCT"), Ct);
        reading.IsCompleted.ShouldBeFalse();

        time.Advance(TimeSpan.FromSeconds(11));

        (await reading).ShouldBeNull();
        session.Com1.ShouldBeNull();
    }

    [Fact]
    public async Task It_tells_the_user_where_it_is_reading_from()
    {
        var messages = new List<string>();
        await using var session = MockSession(messages.Add);

        await session.ReadCallsignAsync(new AppOptions(Source: SimSourceKind.Mock, AtcAirport: "SBCT"), Ct);

        messages.ShouldHaveSingleItem().ShouldContain("simulador");
    }

    [Fact]
    public async Task Closing_the_session_disconnects_from_the_simulator()
    {
        var sim = new MockSimAdapter(TimeProvider.System);
        sim.SetState(MockAircraft.TaxiSequence(1)[0]);
        var session = new SimSession(
            new DelegateFactory(() => new SimSource(sim, Drive: null, EndsWhenDriveCompletes: false, Description: "mock")),
            TimeProvider.System,
            TimeSpan.FromSeconds(30),
            _ => { });
        await session.ReadCallsignAsync(new AppOptions(AtcAirport: "SBCT"), Ct);
        sim.ConnectionState.ShouldBe(SimConnectionState.Connected);

        await session.DisposeAsync();

        sim.ConnectionState.ShouldBe(SimConnectionState.Disconnected);
    }

    [Fact]
    public async Task Closing_a_session_that_never_read_anything_is_harmless()
    {
        await MockSession().DisposeAsync();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5, Ct);
        }
    }
}
