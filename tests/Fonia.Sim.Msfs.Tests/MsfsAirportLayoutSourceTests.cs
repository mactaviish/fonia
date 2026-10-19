using Fonia.Core.Airports;

namespace Fonia.Sim.Msfs.Tests;

public class MsfsAirportLayoutSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MsfsAirportLayoutSource SourceOf(Func<string, CancellationToken, Task<AirportFacility>> read, double seconds = 5) => new(read, TimeSpan.FromSeconds(seconds));

    private static async Task<AirportFacility> SmallAirport() =>
        await new MsfsFacilityReader(() => FakeFacilityTransport.SmallAirport(), TimeSpan.FromMilliseconds(100)).ReadAsync("SBXX", TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_layout_is_made_from_what_the_simulator_says()
    {
        var facility = await SmallAirport();

        var layout = await SourceOf((_, _) => Task.FromResult(facility)).GetAsync("SBXX", Ct);

        layout.ShouldNotBeNull();
        layout.Ident.ShouldBe("SBXX");
        layout.Runways.Count.ShouldBe(1);
        layout.Frequencies.Count.ShouldBe(3);
    }

    [Fact]
    public async Task The_code_is_passed_on_to_the_simulator_as_it_came()
    {
        string? asked = null;
        var facility = await SmallAirport();

        await SourceOf((icao, _) =>
        {
            asked = icao;
            return Task.FromResult(facility);
        }).GetAsync("SBXX", Ct);

        asked.ShouldBe("SBXX");
    }

    [Fact]
    public async Task A_simulator_that_cannot_be_reached_gives_no_map_and_no_error()
    {
        var layout = await SourceOf((_, _) => Task.FromException<AirportFacility>(new FacilityReadException("MSFS is not reachable."))).GetAsync("SBXX", Ct);

        layout.ShouldBeNull();
    }

    [Fact]
    public async Task Any_other_failure_of_the_read_gives_no_map_too()
    {
        var layout = await SourceOf((_, _) => Task.FromException<AirportFacility>(new InvalidOperationException("boom"))).GetAsync("SBXX", Ct);

        layout.ShouldBeNull();
    }

    [Fact]
    public async Task A_simulator_that_takes_too_long_gives_no_map_when_the_time_runs_out()
    {
        var layout = await SourceOf(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return await SmallAirport();
        }, seconds: 0.1).GetAsync("SBXX", Ct);

        layout.ShouldBeNull();
    }

    [Fact]
    public async Task An_airport_the_simulator_does_not_know_gives_no_map()
    {
        var empty = new AirportFacility(null, [], [], [], [], [], [], [], TimeSpan.Zero);

        (await SourceOf((_, _) => Task.FromResult(empty)).GetAsync("ZZZZ", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_is_not_a_failure_to_hide()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => SourceOf(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return await SmallAirport();
        }).GetAsync("SBXX", cancelled.Token));
    }
}
