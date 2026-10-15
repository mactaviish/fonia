using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Channels;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.App.Tests;

// The simulator loads another flight while the text console is running: it follows, with no one asking.
public class FollowFlightTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Airport Make(string ident, string spoken, double latitude, double longitude) => new(
        ident, spoken + " Airport", spoken, AirportType.Large, latitude, longitude, 2000, ident, null, spoken, SpokenNameSource.Reference, true,
        [new Runway(new RunwayEnd("15", 150, null, null), new RunwayEnd("33", 330, null, null), 6000, 150, "ASP", true, false)],
        [new AirportFrequency(AirportFrequencyType.Ground, new Frequency(118000), "Ground")]);

    private static readonly Airport Curitiba = Make("SBCT", "Curitiba", -25.5285, -49.1758);

    private static readonly Airport Guarulhos = Make("SBGR", "Guarulhos", -23.4356, -46.4731);

    private sealed class Catalog : IAirportCatalog
    {
        public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport found)
        {
            found = new[] { Curitiba, Guarulhos }.FirstOrDefault(a => string.Equals(a.Ident, ident, StringComparison.OrdinalIgnoreCase));

            return found is not null;
        }
    }

    // Lines that arrive when the test says so, like a keyboard.
    private sealed class Keyboard : TextReader
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

        public void Type(string line) => _lines.Writer.TryWrite(line);

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _lines.Reader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
    }

    private static AircraftState State(Airport at, int seconds, string registration) => new(
        new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero).AddSeconds(seconds), at.LatitudeDegrees, at.LongitudeDegrees, 3000, 0, 0, 150, true, null, null, 1200, registration, "C172");

    private static async Task WaitForAsync(StringWriter output, string text)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!output.ToString().Contains(text, StringComparison.Ordinal))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"'{text}' did not appear. Output:\n{output}");
            }

            await Task.Delay(5, Ct);
        }
    }

    private static (Task<int> Run, Keyboard Keyboard, Channel<AircraftState> Sim, StringWriter Output, CancellationTokenSource Stop) Start(Func<double, double, (Airport, double)?>? nearest)
    {
        var keyboard = new Keyboard();
        var sim = Channel.CreateUnbounded<AircraftState>();
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var console = new AtcConsole(
            new Catalog(),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            keyboard,
            output,
            new StringWriter(),
            (_, _) => Task.FromResult<string?>(null),
            interactive: false,
            nearestAirport: nearest is null ? null : (lat, lon) => nearest(lat, lon) is { } found ? (found.Item1, found.Item2) : null,
            states: token => sim.Reader.ReadAllAsync(token));

        return (console.RunAsync(new AppOptions(AtcAirport: "SBCT", Callsign: "PR-FRA", NoMetar: true), stop.Token), keyboard, sim, output, stop);
    }

    [Fact]
    public async Task When_the_simulator_loads_a_flight_at_another_airport_the_console_follows_it_and_the_flight_starts_over()
    {
        var (run, keyboard, sim, output, stop) = Start((_, _) => (Guarulhos, 0.6));
        using var _ = stop;

        keyboard.Type("Solo Curitiba, Papa Romeo Foxtrot Romeo Alfa, solicito acionamento");
        await WaitForAsync(output, "SOLO Curitiba: PAPA ROMEO FOXTROT ROMEO ALFA, acionamento aprovado");

        await sim.Writer.WriteAsync(State(Curitiba, 0, "PR-FRA"), Ct);
        await sim.Writer.WriteAsync(State(Guarulhos, 1, "PT-XYZ"), Ct);
        await WaitForAsync(output, "Novo voo detectado no simulador: aeronave PTXYZ, SBGR.");

        // What waited for a readback in the old flight does not follow, and the controller is the one of the new airport.
        keyboard.Type("Solo Guarulhos, Papa Tango Xray Yankee Zulu, solicito acionamento");
        await WaitForAsync(output, "SOLO Guarulhos: PAPA TANGO X-RAY YANKEE ZULU, acionamento aprovado");

        keyboard.Type("sair");
        (await run).ShouldBe(0);
        output.ToString().ShouldContain("Fonia ATC - SBGR Guarulhos Airport. Aeronave PTXYZ.");
    }

    [Fact]
    public async Task A_new_flight_far_from_any_airport_is_told_and_the_airport_stays()
    {
        var (run, keyboard, sim, output, stop) = Start((_, _) => (Guarulhos, 250));
        using var _ = stop;

        await sim.Writer.WriteAsync(State(Curitiba, 0, "PR-FRA"), Ct);
        await sim.Writer.WriteAsync(State(Guarulhos, 1, "PR-FRA"), Ct);
        await WaitForAsync(output, "nenhum aeroporto por perto: o Fonia continua em SBCT");

        keyboard.Type("sair");
        (await run).ShouldBe(0);
    }

    [Fact]
    public async Task The_same_flight_moving_about_changes_nothing()
    {
        var (run, keyboard, sim, output, stop) = Start((_, _) => (Guarulhos, 0.6));
        using var _ = stop;

        await sim.Writer.WriteAsync(State(Curitiba, 0, "PR-FRA"), Ct);
        await sim.Writer.WriteAsync(State(Curitiba, 1, "PR-FRA"), Ct);
        await sim.Writer.WriteAsync(State(Curitiba, 2, "PR-FRA"), Ct);
        keyboard.Type("Solo Curitiba, Papa Romeo Foxtrot Romeo Alfa, solicito acionamento");
        await WaitForAsync(output, "acionamento aprovado");
        keyboard.Type("sair");

        (await run).ShouldBe(0);
        output.ToString().ShouldNotContain("Novo voo");
    }

    [Fact]
    public async Task Without_a_way_to_find_the_nearest_airport_the_console_does_not_follow()
    {
        var (run, keyboard, sim, output, stop) = Start(nearest: null);
        using var _ = stop;

        await sim.Writer.WriteAsync(State(Curitiba, 0, "PR-FRA"), Ct);
        await sim.Writer.WriteAsync(State(Guarulhos, 1, "PT-XYZ"), Ct);
        keyboard.Type("sair");

        (await run).ShouldBe(0);
        output.ToString().ShouldNotContain("Novo voo");
    }
}
