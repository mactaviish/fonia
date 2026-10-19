using System.Diagnostics.CodeAnalysis;
using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.App.Tests;

// An airport that keeps the landings and the takeoffs on different runways (Guarulhos): the console shows both, the Torre clears the landing on
// one and the takeoff on the other, and the user can say which runway lands.
public class AtcConsoleRunwayUseTests
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

    private static Runway Runway(string low, string high, int length) =>
        new(
            new RunwayEnd(low, RunwayDesignator.MagneticHeadingDegrees(low), null, null),
            new RunwayEnd(high, RunwayDesignator.MagneticHeadingDegrees(high), null, null),
            length,
            150,
            "ASP",
            true,
            false);

    // 10L/28R is the longer, so with no wind and nothing asked the runway in use is the 10L.
    private static readonly Airport Parallel = new(
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
        [Runway("10L", "28R", 12000), Runway("10R", "28L", 10000)],
        [
            new AirportFrequency(AirportFrequencyType.Ground, new Frequency(121700), "GND"),
            new AirportFrequency(AirportFrequencyType.Tower, new Frequency(118400), "TWR"),
        ]);

    private static readonly RunwayUseRule[] Rules = [new RunwayUseRule("10R", "10L"), new RunwayUseRule("28L", "28R")];

    private sealed record Result(int ExitCode, string Output, string Error);

    private static async Task<Result> RunAsync(AppOptions options, string typed, bool withRules = true)
    {
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var error = new StringWriter(new StringBuilder()) { NewLine = "\n" };

        var console = new AtcConsole(
            new FakeCatalog(Parallel),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new StringReader(typed),
            output,
            error,
            (_, _) => Task.FromResult<string?>(null),
            interactive: false,
            runwayUse: withRules ? icao => icao == "SBXX" ? Rules : [] : null);

        var code = await console.RunAsync(options, Ct);

        return new Result(code, output.ToString(), error.ToString());
    }

    private static AppOptions Atc(string? runway = null, string? arrival = null, Frequency? com1 = null) =>
        new(AtcAirport: "SBXX", Callsign: "PR-ABC", Runway: runway, ArrivalRunway: arrival, Com1: com1);

    [Fact]
    public async Task The_header_names_the_runway_of_the_takeoffs_and_the_one_of_the_landings()
    {
        var result = await RunAsync(Atc(), "sair\n");

        result.Output.ShouldContain("Pistas em uso: decolagem 10L, pouso 10R.");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_rules_there_is_one_runway_in_use_as_always()
    {
        var result = await RunAsync(Atc(), "sair\n", withRules: false);

        result.Output.ShouldContain("Pista em uso: 10L.");
        result.Output.ShouldNotContain("Pistas em uso");
    }

    [Fact]
    public async Task The_runway_asked_for_as_the_other_direction_brings_its_own_pair()
    {
        var result = await RunAsync(Atc(runway: "28R"), "sair\n");

        result.Output.ShouldContain("Pistas em uso: decolagem 28R, pouso 28L.");
    }

    [Fact]
    public async Task The_user_can_ask_for_the_landing_runway()
    {
        var result = await RunAsync(Atc(arrival: "28L"), "sair\n");

        result.Output.ShouldContain("Pistas em uso: decolagem 10L, pouso 28L.");
    }

    [Fact]
    public async Task A_landing_runway_the_airport_does_not_have_is_refused_with_the_runways_it_has()
    {
        var result = await RunAsync(Atc(arrival: "09"), "sair\n");

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("não tem a pista '09' aberta para os pousos");
        result.Error.ShouldContain("10L/28R");
    }

    [Fact]
    public async Task The_torre_clears_the_circuit_on_the_landing_runway()
    {
        var result = await RunAsync(Atc(com1: new Frequency(118400)), "Torre Curitiba, PRABC, solicito pouso\nsair\n");

        result.Output.ShouldContain("autorizado para o circuito de tráfego, PISTA UNO ZERO DIREITA, reporte perna do vento.");
    }

    [Fact]
    public async Task The_solo_clears_the_taxi_to_the_runway_of_the_takeoffs()
    {
        var result = await RunAsync(Atc(com1: new Frequency(121700)), "Solo Curitiba, PRABC, solicito táxi\nsair\n");

        result.Output.ShouldContain("autorizado táxi para o ponto de espera da PISTA UNO ZERO ESQUERDA.");
    }
}
