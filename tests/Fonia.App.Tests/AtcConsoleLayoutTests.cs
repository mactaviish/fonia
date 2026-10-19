using System.Diagnostics.CodeAnalysis;
using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.App.Tests;

// The map of the airport and the simulator's frequencies in the console: an extra that makes the airport better when there is one, and
// changes nothing when there is none (no simulator, no file kept, --no-map, or a source that fails).
public class AtcConsoleLayoutTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeCatalog(Airport airport) : IAirportCatalog
    {
        public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport found)
        {
            found = string.Equals(airport.Ident, ident, StringComparison.OrdinalIgnoreCase) ? airport : null;

            return found is not null;
        }
    }

    private sealed class FakeLayouts(AirportLayout? layout, Exception? fails = null) : IAirportLayoutSource
    {
        public List<string> Asked { get; } = [];

        public Task<AirportLayout?> GetAsync(string icao, CancellationToken ct)
        {
            Asked.Add(icao);

            return fails is not null ? Task.FromException<AirportLayout?>(fails) : Task.FromResult(layout);
        }
    }

    private static Runway Runway(string low, string high, int length) =>
        new(
            new RunwayEnd(low, RunwayDesignator.MagneticHeadingDegrees(low), null, null),
            new RunwayEnd(high, RunwayDesignator.MagneticHeadingDegrees(high), null, null),
            length,
            150,
            "ASP",
            true,
            false);

    // The airport data: runway 11/29 (the longest, so "11"), a Solo on 118.000 and a Torre on 118.550 (the airport data is wrong about the Torre).
    private static readonly Airport Ours = new(
        "SBXX",
        "SBXX Airport",
        "Curitiba",
        AirportType.Large,
        -25.5,
        -49.1,
        2000,
        "SBXX",
        null,
        "Curitiba",
        SpokenNameSource.Reference,
        true,
        [Runway("15", "33", 6000), Runway("11", "29", 9000)],
        [
            new AirportFrequency(AirportFrequencyType.Ground, new Frequency(118000), "GND"),
            new AirportFrequency(AirportFrequencyType.Tower, new Frequency(118550), "TWR"),
        ]);

    private static GeoPoint Apron => new(-25.5, -49.1);

    // The simulator's map: runway 11/29, a taxiway named A from the apron to a holding point of the runway, and the right Torre.
    private static AirportLayout SimulatorLayout() =>
        new(
            "SBXX",
            [new LayoutRunway(new LayoutRunwayEnd("11", new GeoPoint(-25.4995, -49.0985), 110), new LayoutRunwayEnd("29", new GeoPoint(-25.5005, -49.0955), 290), 1800, 45)],
            [new TaxiNode(1, Apron, TaxiNodeKind.Normal), new TaxiNode(2, new GeoPoint(-25.5, -49.099), TaxiNodeKind.HoldShort, "11/29")],
            [new TaxiEdge(1, 2, "A", TaxiEdgeKind.Taxiway, null)],
            [],
            [
                new AirportFrequency(AirportFrequencyType.Tower, new Frequency(118150), "TORRE"),
                new AirportFrequency(AirportFrequencyType.Delivery, new Frequency(121050), "TRAFEGO"),
            ]);

    private static AircraftState OnTheApron() =>
        new(DateTimeOffset.UtcNow, Apron.LatitudeDegrees, Apron.LongitudeDegrees, 3000, 0, 0, 110, true, null, null, 1200, "PR-ABC", "C172");

    private sealed record Result(int ExitCode, string Output, string Error);

    private static async Task<Result> RunAsync(AppOptions options, string typed, IAirportLayoutSource? layouts, Func<AircraftState?>? aircraft = null)
    {
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var error = new StringWriter(new StringBuilder()) { NewLine = "\n" };

        var console = new AtcConsole(
            new FakeCatalog(Ours),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new StringReader(typed),
            output,
            error,
            (_, _) => Task.FromResult<string?>(null),
            interactive: false,
            liveAircraft: aircraft,
            layouts: layouts);

        var code = await console.RunAsync(options, Ct);

        return new Result(code, output.ToString(), error.ToString());
    }

    private static AppOptions Atc(Frequency? com1 = null, bool noMap = false) => new(AtcAirport: "SBXX", Callsign: "PR-ABC", Com1: com1, NoMap: noMap);

    [Fact]
    public async Task The_header_tells_the_map_was_found_and_the_frequencies_are_the_simulators()
    {
        var result = await RunAsync(Atc(), "sair\n", new FakeLayouts(SimulatorLayout()));

        result.Output.ShouldContain("Mapa do aeródromo (do simulador): 1 taxiways com nome (100% dos caminhos de táxi); 2 frequências do simulador usadas.");
        result.Output.ShouldContain("Ground 118.0, Tower 118.15");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_unit_that_answers_is_the_one_on_the_simulators_frequency_and_not_on_the_one_it_replaced()
    {
        var onTheRight = await RunAsync(Atc(new Frequency(118150)), "sair\n", new FakeLayouts(SimulatorLayout()));
        var onTheWrong = await RunAsync(Atc(new Frequency(118550)), "sair\n", new FakeLayouts(SimulatorLayout()));

        onTheRight.Output.ShouldContain("TORRE Curitiba atende");
        onTheWrong.Output.ShouldNotContain("TORRE Curitiba atende");
    }

    [Fact]
    public async Task Without_the_map_the_airport_data_is_used_as_it_always_was()
    {
        var result = await RunAsync(Atc(new Frequency(118550)), "sair\n", new FakeLayouts(null));

        result.Output.ShouldNotContain("Mapa do aeródromo");
        result.Output.ShouldContain("Ground 118.0, Tower 118.55");
        result.Output.ShouldContain("TORRE Curitiba atende");
    }

    [Fact]
    public async Task Without_any_source_of_maps_nothing_changes()
    {
        var result = await RunAsync(Atc(), "sair\n", layouts: null);

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldNotContain("Mapa do aeródromo");
        result.Output.ShouldContain("Ground 118.0, Tower 118.55");
    }

    [Fact]
    public async Task A_source_that_fails_is_just_no_map_and_never_an_error()
    {
        var result = await RunAsync(Atc(), "sair\n", new FakeLayouts(null, new InvalidOperationException("boom")));

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldNotContain("Mapa do aeródromo");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_no_map_asked_the_source_is_not_even_asked()
    {
        var layouts = new FakeLayouts(SimulatorLayout());

        var result = await RunAsync(Atc(noMap: true), "sair\n", layouts);

        layouts.Asked.ShouldBeEmpty();
        result.Output.ShouldNotContain("Mapa do aeródromo");
        result.Output.ShouldContain("Tower 118.55");
    }

    [Fact]
    public async Task The_source_is_asked_for_the_airport_that_was_chosen()
    {
        var layouts = new FakeLayouts(SimulatorLayout());

        await RunAsync(Atc(), "sair\n", layouts);

        layouts.Asked.ShouldBe(["SBXX"]);
    }

    // Everything together: the map, the position of the aircraft and the Solo's clearance.
    [Fact]
    public async Task With_the_map_and_the_position_the_taxi_clearance_names_the_taxiway()
    {
        var typed = "Solo Curitiba, PRABC, solicito táxi\nsair\n";

        var result = await RunAsync(Atc(new Frequency(118000)), typed, new FakeLayouts(SimulatorLayout()), OnTheApron);

        result.Output.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO UNO, via taxiway ALFA.");
    }

    [Fact]
    public async Task Without_the_position_of_the_aircraft_practising_without_the_simulator_the_clearance_is_the_plain_one()
    {
        var typed = "Solo Curitiba, PRABC, solicito táxi\nsair\n";

        var result = await RunAsync(Atc(new Frequency(118000)), typed, new FakeLayouts(SimulatorLayout()), aircraft: null);

        result.Output.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO UNO.");
        result.Output.ShouldNotContain("via taxiway");
    }

    [Fact]
    public async Task Without_the_map_the_clearance_with_the_position_is_the_plain_one_too()
    {
        var typed = "Solo Curitiba, PRABC, solicito táxi\nsair\n";

        var result = await RunAsync(Atc(new Frequency(118000)), typed, new FakeLayouts(null), OnTheApron);

        result.Output.ShouldNotContain("via taxiway");
    }
}
