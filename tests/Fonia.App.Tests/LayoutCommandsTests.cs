using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Fonia.Core.Airports;
using Fonia.Core.Sim;
using Fonia.Sim.Msfs;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.App.Tests;

// fonia --dump-layout <ICAO>: asks the simulator what it knows about an airport, saves the answer as it came and shows what was found.
public sealed class LayoutCommandsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fonia-layout-test-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeSource(AirportFacility? facility, Exception? fails = null) : IFacilitySource
    {
        public bool? IsMsfs2024 => false;

        public Task<AirportFacility> ReadAsync(string icao, CancellationToken ct) =>
            fails is not null ? Task.FromException<AirportFacility>(fails) : Task.FromResult(facility!);
    }

    private sealed class FakeCatalog(params Airport[] airports) : IAirportCatalog
    {
        public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport airport)
        {
            airport = airports.FirstOrDefault(a => string.Equals(a.Ident, ident, StringComparison.OrdinalIgnoreCase));

            return airport is not null;
        }
    }

    private static Airport OurAirport(params AirportFrequency[] frequencies) =>
        new("SBXX", "Test Airport", "Curitiba", AirportType.Large, -25.5, -49.1, 900, "SBXX", null, "Curitiba", SpokenNameSource.Reference, true, [], frequencies);

    private static AirportFrequency Ours(AirportFrequencyType type, int kHz) => new(type, new Frequency(kHz), type.ToString());

    // A runway 1000 m long at 90 degrees, its center 300 m east and 200 m north of the reference point, and two taxi points on it (X east, Z north).
    private static AirportFacility Facility(params RawFrequency[] frequencies) =>
        new(
            new RawAirport("SBXX", "SB", "Curitiba Test", -25.5, -49.1, 900, 0, 1, frequencies.Length, 3, 0, 2, 1),
            [new RawRunway(-25.5 + (200 / 111_320.0), -49.1 + (300 / (111_320.0 * Math.Cos(-25.5 * Math.PI / 180))), 900, 90, 1000, 45, 4, 9, 0, 27, 0)],
            frequencies,
            [new RawTaxiPoint(0, 1, 0, 100, 200), new RawTaxiPoint(1, 1, 0, 600, 200), new RawTaxiPoint(2, 2, 0, 250, 40)],
            [new RawTaxiPath(2, 45, 9, 0, 0, 1, 0), new RawTaxiPath(1, 23, 0, 0, 1, 2, 0)],
            [new RawTaxiName(0, "A")],
            [],
            [new FacilityRequestOutcome("RUNWAY", 1, true, TimeSpan.FromMilliseconds(30), [])],
            TimeSpan.FromSeconds(1.2));

    private static RawFrequency Sim(int type, int kHz) => new(type, kHz * 1000, "x");

    private async Task<(int Code, string Out, string Err)> RunAsync(IFacilitySource source, params Airport[] catalog)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await LayoutCommands.DumpAsync("SBXX", source, new FakeCatalog(catalog), _root, new FakeTimeProvider(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero)), output, error, Ct);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task The_raw_dump_is_saved_where_the_layouts_go_and_says_who_and_when()
    {
        var (code, output, _) = await RunAsync(new FakeSource(Facility(Sim(6, 118275))), OurAirport());

        code.ShouldBe(0);

        var path = Path.Combine(_root, "raw", "SBXX.raw.json");
        File.Exists(path).ShouldBeTrue();
        output.ShouldContain(path);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(path, Ct))!;
        json["capture"]!["tool"]!.GetValue<string>().ShouldStartWith("fonia ");
        json["capture"]!["at"]!.GetValue<DateTimeOffset>().ShouldBe(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
        json["facility"]!["frequencies"]![0]!["hertz"]!.GetValue<int>().ShouldBe(118_275_000);
    }

    [Fact]
    public async Task The_summary_says_what_came_against_what_the_simulator_says_it_has()
    {
        var (_, output, _) = await RunAsync(new FakeSource(Facility()), OurAirport());

        output.ShouldContain("Aeroporto: SBXX (SB) - Curitiba Test");
        output.ShouldContain("MSFS 2020");
        output.ShouldContain("Pistas:           recebi 1 (o simulador diz que tem 1)");
        output.ShouldContain("Pontos de táxi:   recebi 3 (diz 3), dos quais 1 de espera");
        output.ShouldContain("1 táxi (tipo 1), 1 pista");
        output.ShouldContain("Caminhos de táxi (tipos 1 e 4) com nome: 1 de 1 (A: 1)");
        output.ShouldContain("Nomes de taxiway: recebi 1 (diz 1): A");
    }

    // What MSFS 2020 gave for SBCT: the taxiways are paths of type 4, and most of them have the empty name.
    [Fact]
    public async Task The_taxiways_that_have_a_name_are_counted_among_the_paths_of_type_1_and_4()
    {
        var facility = Facility() with
        {
            TaxiNames = [new RawTaxiName(0, string.Empty), new RawTaxiName(1, "A"), new RawTaxiName(2, "B")],
            TaxiPaths =
            [
                new RawTaxiPath(4, 25, 0, 0, 0, 1, 0),
                new RawTaxiPath(4, 25, 0, 0, 1, 2, 0),
                new RawTaxiPath(4, 25, 0, 0, 2, 3, 1),
                new RawTaxiPath(4, 25, 0, 0, 3, 4, 2),
                new RawTaxiPath(6, 6, 0, 0, 4, 5, 0),
                new RawTaxiPath(2, 45, 9, 0, 0, 1, 0),
            ],
        };

        var (_, output, _) = await RunAsync(new FakeSource(facility), OurAirport());

        output.ShouldContain("1 pista, 4 caminho (tipo 4), 1 veículo");
        output.ShouldContain("Caminhos de táxi (tipos 1 e 4) com nome: 2 de 4 (A: 1, B: 1)");
    }

    [Fact]
    public async Task A_request_that_went_wrong_is_shown_with_its_problems()
    {
        var facility = Facility() with
        {
            Requests = [new FacilityRequestOutcome("TAXI_PATH", 0, false, TimeSpan.Zero, ["definition refused by SimConnect (code 0x80004005)"])],
        };

        var (_, output, _) = await RunAsync(new FakeSource(facility), OurAirport());

        output.ShouldContain("! TAXI_PATH: definition refused by SimConnect (code 0x80004005) (sem fim de dados)");
    }

    [Fact]
    public async Task The_frequencies_are_compared_with_the_ones_of_the_airport_data()
    {
        var facility = Facility(Sim(6, 118275), Sim(5, 121900), Sim(9, 119950), Sim(1, 127800));
        var ours = OurAirport(
            Ours(AirportFrequencyType.Tower, 118275),
            Ours(AirportFrequencyType.Approach, 119950),
            Ours(AirportFrequencyType.Atis, 127800),
            Ours(AirportFrequencyType.Delivery, 121000));

        var (_, output, _) = await RunAsync(new FakeSource(facility), ours);

        output.ShouldContain("TOWER      118.275");
        output.ShouldContain("igual no OurAirports (Tower)");
        output.ShouldContain("GROUND     121.900");
        output.ShouldContain("só no simulador");
        output.ShouldContain("DEPARTURE  119.950");
        output.ShouldContain("igual no OurAirports (Approach)");
        output.ShouldContain("OurAirports: Delivery   121.000");
        output.ShouldContain("só no OurAirports");
    }

    [Fact]
    public async Task The_same_frequency_with_another_type_is_pointed_out()
    {
        var (_, output, _) = await RunAsync(new FakeSource(Facility(Sim(6, 118200))), OurAirport(Ours(AirportFrequencyType.Ground, 118200)));

        output.ShouldContain("mesma frequência, tipo diferente no OurAirports (Ground)");
    }

    [Fact]
    public async Task An_airport_that_the_airport_data_does_not_have_has_nothing_to_compare_with()
    {
        var (_, output, _) = await RunAsync(new FakeSource(Facility(Sim(6, 118275))));

        output.ShouldContain("SBXX não está no OurAirports; nada a comparar");
        output.ShouldContain("TOWER      118.275");
    }

    [Fact]
    public async Task The_check_of_the_axes_says_which_convention_fits_and_whether_it_can_be_trusted()
    {
        var (_, output, _) = await RunAsync(new FakeSource(Facility()), OurAirport());

        output.ShouldContain("melhor: X east, Z north, erro médio 0.0 m");
        output.ShouldContain("a convenção é a melhor, com folga");
    }

    [Fact]
    public async Task Without_runway_paths_the_axes_cannot_be_checked_and_it_says_so()
    {
        var (_, output, _) = await RunAsync(new FakeSource(Facility() with { TaxiPaths = [] }), OurAirport());

        output.ShouldContain("não deu para conferir");
    }

    // A parking spot, taxiway A (named) north to a holding point 100 m from runway 15/33, which has a chain of runway nodes along it.
    private static AirportFacility RoutableFacility(bool withParking = true)
    {
        RawTaxiPoint P(int index, int type, double east, double north) => new(index, type, 0, (float)east, (float)north);

        return new AirportFacility(
            new RawAirport("SBXX", "SB", "Curitiba Test", -25.5, -49.1, 900, 0, 1, 0, 7, 1, 7, 2),
            [new RawRunway(-25.5, -49.1, 900, 315, 1000, 45, 4, 33, 0, 15, 0)],
            [],
            [P(0, 1, 320, -690), P(1, 1, 320, -500), P(2, 5, 320, -180), P(5, 1, -353, 353), P(6, 1, 0, 0), P(8, 1, 250, -250), P(7, 1, 353, -353)],
            [
                new RawTaxiPath(3, 25, 0, 0, 0, 0, 0),
                new RawTaxiPath(4, 25, 0, 0, 0, 1, 1),
                new RawTaxiPath(4, 25, 0, 0, 1, 2, 1),
                new RawTaxiPath(4, 25, 0, 0, 2, 8, 0),
                new RawTaxiPath(2, 45, 33, 0, 5, 6, 0),
                new RawTaxiPath(2, 45, 33, 0, 6, 8, 0),
                new RawTaxiPath(2, 45, 33, 0, 8, 7, 0),
            ],
            [new RawTaxiName(0, string.Empty), new RawTaxiName(1, "A")],
            withParking ? [new RawParking(0, 3, 1, 1, 0, 1, 0, 180, 20, 320, -700)] : [],
            [],
            TimeSpan.FromSeconds(0.3));
    }

    [Fact]
    public async Task The_layout_made_from_the_data_is_saved_next_to_the_raw_dump_and_reads_back()
    {
        var (code, output, _) = await RunAsync(new FakeSource(RoutableFacility()), OurAirport());

        code.ShouldBe(0);

        var path = Path.Combine(_root, "SBXX.json");
        File.Exists(path).ShouldBeTrue();
        output.ShouldContain($"Mapa gravado em {path}");

        var layout = AirportLayoutJson.Read(await File.ReadAllTextAsync(path, Ct));
        layout.Ident.ShouldBe("SBXX");
        layout.Runways.Single().Name.ShouldBe("15/33");
        layout.Nodes.Single(n => n.Id == 2).Runway.ShouldBe("15/33");
    }

    [Fact]
    public async Task The_summary_of_the_map_counts_the_nodes_edges_and_the_holding_points_tied_to_a_runway()
    {
        var (_, output, _) = await RunAsync(new FakeSource(RoutableFacility()), OurAirport());

        output.ShouldContain("Mapa convertido: 8 nós e 7 arestas (3 de taxiway, 3 de pista, 1 de estacionamento); 1 pontos de espera, 1 ligados a uma pista.");
        output.ShouldContain("1 of 3 taxiway paths have no name");
    }

    [Fact]
    public async Task The_test_of_routes_tries_every_parking_spot_against_every_runway_end_and_says_how_it_went()
    {
        var (_, output, _) = await RunAsync(new FakeSource(RoutableFacility()), OurAirport());

        output.ShouldContain("Teste de rotas (de cada um dos 1 estacionamentos ligados");
        output.ShouldContain("cabeceira 15: 1 de 1 com rota (0 atravessando pista)");
        output.ShouldContain("cabeceira 33: 1 de 1 com rota (0 atravessando pista)");
    }

    [Fact]
    public async Task A_route_that_cannot_be_said_is_counted_with_its_reason()
    {
        // Taxiway A without a name: the only way to the holding point is over unnamed taxiway.
        var facility = RoutableFacility() with { TaxiNames = [new RawTaxiName(0, string.Empty), new RawTaxiName(1, string.Empty)] };

        var (_, output, _) = await RunAsync(new FakeSource(facility), OurAirport());

        output.ShouldContain("cabeceira 15: 0 de 1 com rota (0 atravessando pista); sem rota: taxiway sem nome 1");
    }

    [Fact]
    public async Task Without_parking_spots_in_the_graph_there_are_no_routes_to_try()
    {
        var (_, output, _) = await RunAsync(new FakeSource(RoutableFacility(withParking: false)), OurAirport());

        output.ShouldContain("Teste de rotas: sem estacionamento ligado ao grafo.");
    }

    [Fact]
    public async Task When_the_data_cannot_make_a_map_the_report_says_why_and_no_map_is_saved()
    {
        var noRunway = RoutableFacility() with { Runways = [] };

        var (code, output, _) = await RunAsync(new FakeSource(noRunway), OurAirport());

        code.ShouldBe(1);
        output.ShouldContain("Mapa: não deu para converter");
        output.ShouldContain("no runway the layout can use");
        File.Exists(Path.Combine(_root, "SBXX.json")).ShouldBeFalse();
        File.Exists(Path.Combine(_root, "raw", "SBXX.raw.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task An_airport_the_simulator_does_not_have_fails_but_is_still_saved_for_a_look()
    {
        var empty = new AirportFacility(null, [], [], [], [], [], [], [new FacilityRequestOutcome("AIRPORT", 0, true, TimeSpan.Zero, [])], TimeSpan.Zero);

        var (code, output, _) = await RunAsync(new FakeSource(empty), OurAirport());

        code.ShouldBe(1);
        output.ShouldContain("O simulador não mandou o aeroporto");
        File.Exists(Path.Combine(_root, "raw", "SBXX.raw.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_simulator_that_cannot_be_reached_fails_with_a_message_and_saves_nothing()
    {
        var (code, _, error) = await RunAsync(new FakeSource(null, new FacilityReadException("MSFS is not reachable. Is it running?")));

        code.ShouldBe(1);
        error.ShouldContain("não consegui falar com o MSFS");
        error.ShouldContain("MSFS is not reachable");
        Directory.Exists(_root).ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelling_ends_with_a_message_and_a_failure()
    {
        var (code, _, error) = await RunAsync(new FakeSource(null, new OperationCanceledException()));

        code.ShouldBe(1);
        error.ShouldContain("cancelado");
    }
}
