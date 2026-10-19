using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Data.Tests;

public sealed class CachedAirportLayoutSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fonia-layout-cache-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeSource(AirportLayout? layout, Exception? fails = null) : IAirportLayoutSource
    {
        public int Calls { get; private set; }

        public Task<AirportLayout?> GetAsync(string icao, CancellationToken ct)
        {
            Calls++;

            return fails is not null ? Task.FromException<AirportLayout?>(fails) : Task.FromResult(layout);
        }
    }

    private static AirportLayout Layout(string ident = "SBXX", int tower = 118150) =>
        new(
            ident,
            [new LayoutRunway(new LayoutRunwayEnd("11", new GeoPoint(-25.5, -49.1), 110), new LayoutRunwayEnd("29", new GeoPoint(-25.5, -49.0), 290), 1800, 45)],
            [new TaxiNode(1, new GeoPoint(-25.5, -49.1), TaxiNodeKind.Normal), new TaxiNode(2, new GeoPoint(-25.5, -49.09), TaxiNodeKind.Normal)],
            [new TaxiEdge(1, 2, "A", TaxiEdgeKind.Taxiway, null)],
            [],
            [new AirportFrequency(AirportFrequencyType.Tower, new Frequency(tower), "TORRE")]);

    private CachedAirportLayoutSource Cached(IAirportLayoutSource? inner) => new(inner, _directory);

    [Fact]
    public async Task What_the_simulator_says_is_returned_and_kept_in_the_file_of_the_layouts()
    {
        var layout = await Cached(new FakeSource(Layout())).GetAsync("SBXX", Ct);

        layout.ShouldNotBeNull();
        layout.Frequencies.Single().Frequency.KHz.ShouldBe(118150);

        var path = Path.Combine(_directory, "SBXX.json");
        File.Exists(path).ShouldBeTrue();
        AirportLayoutJson.Read(await File.ReadAllTextAsync(path, Ct)).Ident.ShouldBe("SBXX");
        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_simulator_is_asked_every_time_and_what_it_says_replaces_the_file()
    {
        var inner = new FakeSource(Layout(tower: 118150));
        await Cached(inner).GetAsync("SBXX", Ct);

        var newer = new FakeSource(Layout(tower: 118200));
        var layout = await Cached(newer).GetAsync("SBXX", Ct);

        layout!.Frequencies.Single().Frequency.KHz.ShouldBe(118200);
        AirportLayoutJson.Read(await File.ReadAllTextAsync(Path.Combine(_directory, "SBXX.json"), Ct)).Frequencies.Single().Frequency.KHz.ShouldBe(118200);
        inner.Calls.ShouldBe(1);
        newer.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task When_the_simulator_gives_none_the_file_kept_before_is_used()
    {
        await Cached(new FakeSource(Layout())).GetAsync("SBXX", Ct);

        var layout = await Cached(new FakeSource(null)).GetAsync("SBXX", Ct);

        layout.ShouldNotBeNull();
        layout.Ident.ShouldBe("SBXX");
    }

    [Fact]
    public async Task When_the_simulator_fails_the_file_kept_before_is_used_and_no_error_comes_out()
    {
        await Cached(new FakeSource(Layout())).GetAsync("SBXX", Ct);

        var layout = await Cached(new FakeSource(null, new InvalidOperationException("boom"))).GetAsync("SBXX", Ct);

        layout.ShouldNotBeNull();
    }

    [Fact]
    public async Task Without_a_simulator_only_the_file_is_used_to_practise_without_it()
    {
        await Cached(new FakeSource(Layout())).GetAsync("SBXX", Ct);

        var layout = await Cached(null).GetAsync("sbxx", Ct);

        layout.ShouldNotBeNull();
        layout.Ident.ShouldBe("SBXX");
    }

    [Fact]
    public async Task With_neither_the_simulator_nor_a_file_there_is_no_map()
    {
        (await Cached(null).GetAsync("SBXX", Ct)).ShouldBeNull();
        (await Cached(new FakeSource(null)).GetAsync("SBXX", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_file_that_is_broken_is_no_map_and_not_an_error()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "SBXX.json"), "{ not a layout", Ct);

        (await Cached(null).GetAsync("SBXX", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_file_of_another_airport_is_not_taken_for_this_one()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "SBXX.json"), AirportLayoutJson.Write(Layout("SBYY")), Ct);

        (await Cached(null).GetAsync("SBXX", Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData("..\\SBXX")]
    [InlineData("../SBXX")]
    [InlineData("SB")]
    [InlineData("")]
    [InlineData("SBXX.json")]
    [InlineData("SB XX")]
    public async Task A_code_that_is_not_an_airport_code_is_never_made_a_file_name(string icao)
    {
        var inner = new FakeSource(Layout());

        (await Cached(inner).GetAsync(icao, Ct)).ShouldBeNull();
        inner.Calls.ShouldBe(0);
        Directory.Exists(_directory).ShouldBeFalse();
    }

    [Fact]
    public async Task Failing_to_keep_the_file_does_not_stop_the_layout_from_being_used()
    {
        // The directory is a file: it cannot be created.
        await File.WriteAllTextAsync(_directory, "in the way", Ct);

        try
        {
            var layout = await new CachedAirportLayoutSource(new FakeSource(Layout()), _directory).GetAsync("SBXX", Ct);

            layout.ShouldNotBeNull();
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    [Fact]
    public async Task Cancelling_is_passed_on()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Cached(new FakeSource(null, new OperationCanceledException(cancelled.Token))).GetAsync("SBXX", cancelled.Token));
    }
}
