namespace Fonia.Sim.Msfs.Tests;

public class MsfsFacilityReaderTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MsfsFacilityReader ReaderOf(FakeFacilityTransport transport, TimeSpan? timeout = null) => new(() => transport, timeout ?? Short);

    [Fact]
    public async Task The_airport_comes_back_with_everything_the_simulator_sent()
    {
        var facility = await ReaderOf(FakeFacilityTransport.SmallAirport()).ReadAsync("SBXX", Ct);

        facility.Airport.ShouldNotBeNull();
        facility.Airport.Icao.ShouldBe("SBXX");
        facility.Airport.Name.ShouldBe("Curitiba Test");
        facility.Airport.LatitudeDegrees.ShouldBe(-25.5);
        facility.Airport.TaxiPointCount.ShouldBe(3);
        facility.Runways.Single().PrimaryNumber.ShouldBe(11);
        facility.Runways.Single().SecondaryNumber.ShouldBe(29);
        facility.Runways.Single().LengthMeters.ShouldBe(1800);
        facility.Frequencies.Select(f => (f.TypeName, f.KiloHertz)).ShouldBe([("TOWER", 118275), ("GROUND", 121900), ("ATIS", 127800)]);
        facility.TaxiPoints.Select(p => (p.Index, p.Type)).ShouldBe([(0, 1), (1, 2), (2, 1)]);
        facility.TaxiPaths.Select(p => (p.Type, p.Start, p.End)).ShouldBe([(1, 0, 1), (2, 1, 2)]);
        facility.TaxiNames.Single().Name.ShouldBe("A");
        facility.Parkings.Single().Number.ShouldBe(3u);
        facility.Parkings.Single().BiasZ.ShouldBe(75);
    }

    [Fact]
    public async Task Each_part_of_the_airport_is_its_own_request_and_every_one_says_how_it_went()
    {
        var transport = FakeFacilityTransport.SmallAirport();

        var facility = await ReaderOf(transport).ReadAsync("SBXX", Ct);

        transport.Requested.ShouldBe(["AIRPORT:SBXX", "RUNWAY:SBXX", "FREQUENCY:SBXX", "TAXI_POINT:SBXX", "TAXI_PATH:SBXX", "TAXI_NAME:SBXX", "TAXI_PARKING:SBXX"]);
        facility.Requests.Select(r => r.Name).ShouldBe(["AIRPORT", "RUNWAY", "FREQUENCY", "TAXI_POINT", "TAXI_PATH", "TAXI_NAME", "TAXI_PARKING"]);
        facility.Requests.ShouldAllBe(r => r.Ended && r.Problems.Count == 0);
        facility.Requests.Single(r => r.Name == "TAXI_POINT").Items.ShouldBe(3);
    }

    [Fact]
    public async Task The_code_is_asked_in_capitals_and_without_spaces()
    {
        var transport = FakeFacilityTransport.SmallAirport();

        await ReaderOf(transport).ReadAsync("  sbxx ", Ct);

        transport.Requested.ShouldAllBe(r => r.EndsWith(":SBXX", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_part_the_simulator_refuses_costs_only_that_part()
    {
        var transport = FakeFacilityTransport.SmallAirport();
        transport.RefuseDefinition.Add("TAXI_PATH");

        var facility = await ReaderOf(transport).ReadAsync("SBXX", Ct);

        facility.TaxiPaths.ShouldBeEmpty();
        facility.TaxiPoints.Count.ShouldBe(3);
        facility.Runways.Count.ShouldBe(1);
        facility.Requests.Single(r => r.Name == "TAXI_PATH").Problems.ShouldHaveSingleItem().ShouldContain("definition refused");
    }

    [Fact]
    public async Task A_request_that_never_ends_is_given_up_on_and_says_so()
    {
        var transport = FakeFacilityTransport.SmallAirport();
        transport.NeverEnd.Add("TAXI_NAME");

        var facility = await ReaderOf(transport, TimeSpan.FromMilliseconds(50)).ReadAsync("SBXX", Ct);

        var outcome = facility.Requests.Single(r => r.Name == "TAXI_NAME");
        outcome.Ended.ShouldBeFalse();
        outcome.Problems.ShouldContain(p => p.Contains("no end of data", StringComparison.Ordinal));
        facility.TaxiNames.Single().Name.ShouldBe("A");
        facility.Parkings.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_exception_of_the_simulator_is_noted_with_its_code_and_parameter()
    {
        var transport = FakeFacilityTransport.SmallAirport();
        transport.RaiseException.Add("RUNWAY");

        var facility = await ReaderOf(transport).ReadAsync("SBXX", Ct);

        facility.Requests.Single(r => r.Name == "RUNWAY").Problems.ShouldHaveSingleItem().ShouldContain("exception 21 (parameter 3");
        facility.Requests.Single(r => r.Name == "FREQUENCY").Problems.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_code_that_matches_more_than_one_facility_is_noted()
    {
        var transport = FakeFacilityTransport.SmallAirport();
        transport.Ambiguous.Add("AIRPORT");

        var facility = await ReaderOf(transport).ReadAsync("SBXX", Ct);

        facility.Airport.ShouldBeNull();
        facility.Requests[0].Problems.ShouldHaveSingleItem().ShouldContain("more than one facility");
    }

    [Fact]
    public async Task An_item_shorter_than_its_fields_is_skipped_and_noted()
    {
        var transport = FakeFacilityTransport.SmallAirport();
        transport.Items["RUNWAY"] = [(0, FacilityTestData.Runway()[..20])];

        var facility = await ReaderOf(transport).ReadAsync("SBXX", Ct);

        facility.Runways.ShouldBeEmpty();
        facility.Requests.Single(r => r.Name == "RUNWAY").Problems.ShouldHaveSingleItem().ShouldContain("20 bytes");
    }

    [Fact]
    public async Task An_airport_the_simulator_does_not_know_comes_back_empty()
    {
        var transport = new FakeFacilityTransport();

        var facility = await ReaderOf(transport).ReadAsync("ZZZZ", Ct);

        facility.Airport.ShouldBeNull();
        facility.Runways.ShouldBeEmpty();
        facility.Requests.ShouldAllBe(r => r.Ended && r.Items == 0);
    }

    [Fact]
    public async Task The_simulator_version_is_kept_and_the_connection_is_closed()
    {
        var transport = FakeFacilityTransport.SmallAirport();
        transport.IsMsfs2024 = false;
        var reader = ReaderOf(transport);

        await reader.ReadAsync("SBXX", Ct);

        reader.IsMsfs2024.ShouldBe(false);
        transport.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task When_the_simulator_cannot_be_reached_the_reader_says_so_with_the_facility_exception()
    {
        var transport = new FakeFacilityTransport { OpenFails = new FacilityReadException("MSFS is not reachable.") };

        await Should.ThrowAsync<FacilityReadException>(() => ReaderOf(transport).ReadAsync("SBXX", Ct));
        transport.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancelling_stops_the_read()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => ReaderOf(FakeFacilityTransport.SmallAirport()).ReadAsync("SBXX", cancelled.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_code_that_is_empty_is_refused(string icao)
    {
        await Should.ThrowAsync<ArgumentException>(() => ReaderOf(new FakeFacilityTransport()).ReadAsync(icao, Ct));
    }
}
