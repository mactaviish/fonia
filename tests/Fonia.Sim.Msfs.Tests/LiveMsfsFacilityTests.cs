namespace Fonia.Sim.Msfs.Tests;

/// <summary>
/// Asks the real MSFS 2020 for an airport (read only). Skipped unless <c>FONIA_LIVE_MSFS=1</c>. The airport is <c>FONIA_LIVE_AIRPORT</c>
/// (default SBCT).
/// <code>
/// $env:FONIA_LIVE_MSFS = "1"; dotnet test --filter-class "*LiveMsfsFacilityTests"
/// </code>
/// </summary>
[Trait("Category", "Live")]
public class LiveMsfsFacilityTests
{
    private const string LiveSkipReason = "Needs a running MSFS 2020 with a flight loaded. Set FONIA_LIVE_MSFS=1 to run.";

    public static bool LiveEnabled => Environment.GetEnvironmentVariable("FONIA_LIVE_MSFS") == "1";

    [Fact(Skip = LiveSkipReason, SkipUnless = nameof(LiveEnabled), SkipType = typeof(LiveMsfsFacilityTests))]
    public async Task The_real_simulator_gives_the_airport_its_runways_and_its_requests_end()
    {
        var icao = Environment.GetEnvironmentVariable("FONIA_LIVE_AIRPORT") ?? "SBCT";

        var facility = await new MsfsFacilityReader().ReadAsync(icao, TestContext.Current.CancellationToken);

        facility.Airport.ShouldNotBeNull();
        facility.Airport.Icao.ShouldBe(icao);
        facility.Runways.ShouldNotBeEmpty();
        facility.Requests.ShouldAllBe(r => r.Ended, string.Join("; ", facility.Requests.Where(r => !r.Ended).Select(r => $"{r.Name}: {string.Join(", ", r.Problems)}")));
        facility.Requests.SelectMany(r => r.Problems).ShouldBeEmpty();
        facility.Airport.RunwayCount.ShouldBe(facility.Runways.Count);
        facility.Airport.TaxiPointCount.ShouldBe(facility.TaxiPoints.Count);
        facility.Airport.TaxiPathCount.ShouldBe(facility.TaxiPaths.Count);
    }
}
