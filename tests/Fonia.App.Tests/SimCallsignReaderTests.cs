using Fonia.Sim.Mock;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.App.Tests;

public class SimCallsignReaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class DelegateFactory(Func<SimSource> create) : ISimSourceFactory
    {
        public SimSource Create(AppOptions options, Stream? replay) => create();
    }

    [Fact]
    public async Task It_reads_the_registration_of_the_first_state()
    {
        var reader = new SimCallsignReader(
            new SimSourceFactory(NullLoggerFactory.Instance, TimeProvider.System),
            TimeProvider.System,
            TimeSpan.FromSeconds(30),
            _ => { });

        var registration = await reader.ReadAsync(new AppOptions(Source: SimSourceKind.Mock, AtcAirport: "SBCT"), Ct);

        registration.ShouldBe("PR-ABC");
    }

    [Fact]
    public async Task It_gives_up_with_null_when_no_state_comes_in_time()
    {
        var time = new FakeTimeProvider();
        var silent = new MockSimAdapter(time);
        var reader = new SimCallsignReader(
            new DelegateFactory(() => new SimSource(silent, Drive: null, EndsWhenDriveCompletes: false, Description: "silent")),
            time,
            TimeSpan.FromSeconds(10),
            _ => { });

        var reading = reader.ReadAsync(new AppOptions(AtcAirport: "SBCT"), Ct);
        reading.IsCompleted.ShouldBeFalse();

        time.Advance(TimeSpan.FromSeconds(11));

        (await reading).ShouldBeNull();
    }

    [Fact]
    public async Task It_tells_the_user_where_it_is_reading_from()
    {
        var messages = new List<string>();
        var reader = new SimCallsignReader(
            new SimSourceFactory(NullLoggerFactory.Instance, TimeProvider.System),
            TimeProvider.System,
            TimeSpan.FromSeconds(30),
            messages.Add);

        await reader.ReadAsync(new AppOptions(Source: SimSourceKind.Mock, AtcAirport: "SBCT"), Ct);

        messages.ShouldHaveSingleItem().ShouldContain("simulador");
    }
}
