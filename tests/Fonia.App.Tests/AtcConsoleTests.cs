using System.Diagnostics.CodeAnalysis;
using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.App.Tests;

public class AtcConsoleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeCatalog(params Airport[] airports) : IAirportCatalog
    {
        public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport airport)
        {
            airport = airports.FirstOrDefault(a => string.Equals(a.Ident, ident, StringComparison.OrdinalIgnoreCase));
            return airport is not null;
        }
    }

    private static Runway Runway(string low, string high, int length, bool closed = false) =>
        new(
            new RunwayEnd(low, RunwayDesignator.MagneticHeadingDegrees(low), null, null),
            new RunwayEnd(high, RunwayDesignator.MagneticHeadingDegrees(high), null, null),
            length,
            150,
            "ASP",
            true,
            closed);

    private static Airport Airport(string ident, string name, AirportFrequencyType[] frequencies, params Runway[] runways) =>
        new(
            ident,
            $"{name} Airport",
            name,
            AirportType.Large,
            -25.5,
            -49.1,
            2000,
            ident,
            null,
            name,
            SpokenNameSource.Reference,
            true,
            runways,
            [.. frequencies.Select((f, i) => new AirportFrequency(f, new Frequency(118000 + (i * 1500)), f.ToString()))]);

    private static readonly Airport Solo = Airport(
        "SBXX",
        "Curitiba",
        [AirportFrequencyType.Ground, AirportFrequencyType.Tower],
        Runway("15", "33", 6000),
        Runway("11", "29", 9000));

    private sealed record Result(int ExitCode, string Output, string Error);

    private static async Task<Result> RunAsync(
        AppOptions options,
        string typed,
        Airport? airport = null,
        Func<AppOptions, CancellationToken, Task<string?>>? callsignFromSim = null)
    {
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var error = new StringWriter(new StringBuilder()) { NewLine = "\n" };

        var console = new AtcConsole(
            new FakeCatalog(airport ?? Solo),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new StringReader(typed),
            output,
            error,
            callsignFromSim ?? ((_, _) => Task.FromResult<string?>(null)),
            interactive: false);

        var code = await console.RunAsync(options, Ct);

        return new Result(code, output.ToString(), error.ToString());
    }

    private static AppOptions Atc(string icao = "SBXX", string? runway = null, string? callsign = "PR-ABC") =>
        new(AtcAirport: icao, Runway: runway, Callsign: callsign);

    [Fact]
    public async Task It_introduces_the_airport_who_answers_the_runway_and_the_frequencies()
    {
        var result = await RunAsync(Atc(), "sair\n");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("SBXX Curitiba Airport");
        result.Output.ShouldContain("Aeronave PRABC");
        result.Output.ShouldContain("Responde: SOLO Curitiba. Pista em uso: 11.");
        result.Output.ShouldContain("Ground 118.0, Tower 119.5");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_controller_answers_in_text_with_who_speaks()
    {
        var result = await RunAsync(Atc(), "Solo Curitiba, PRABC, solicito acionamento\nsair\n");

        result.Output.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado, chame pronto para o táxi.");
    }

    [Fact]
    public async Task A_full_cycle_with_readbacks_ends_without_problems()
    {
        var typed = string.Join(
            '\n',
            "solicito pushback e acionamento, PRABC",
            "autorizado pushback e acionamento, PRABC",
            "PRABC, pronto para o táxi",
            "autorizado táxi pista um um, PRABC",
            "sair",
            string.Empty);

        var result = await RunAsync(Atc(), typed);

        var lines = result.Output.Split('\n');
        lines.ShouldContain(l => l.StartsWith("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, autorizado pushback e acionamento", StringComparison.Ordinal));
        lines.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO UNO.");
        result.Output.Split("(o controlador não responde)").Length.ShouldBe(3, "the two correct readbacks get no answer");
    }

    [Fact]
    public async Task A_wrong_readback_is_corrected()
    {
        var typed = "PRABC, solicito táxi\ntáxi pista três três, PRABC\nsair\n";

        var result = await RunAsync(Atc(), typed);

        result.Output.ShouldContain("negativo, autorizado táxi para o ponto de espera da PISTA UNO UNO.");
    }

    [Fact]
    public async Task What_is_not_understood_is_answered_with_repita_and_blank_lines_are_ignored()
    {
        var result = await RunAsync(Atc(), "\n   \nblá blá\nsair\n");

        result.Output.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, repita.");
        result.Output.Split('\n').Count(l => l.StartsWith("SOLO", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Theory]
    [InlineData("sair")]
    [InlineData("SAIR")]
    [InlineData("exit")]
    [InlineData("  quit  ")]
    public async Task The_exit_words_end_the_session_without_reading_more(string word)
    {
        var result = await RunAsync(Atc(), $"{word}\nsolicito acionamento, PRABC\n");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldNotContain("acionamento aprovado");
    }

    [Fact]
    public async Task The_end_of_the_input_ends_the_session()
    {
        (await RunAsync(Atc(), "solicito acionamento, PRABC\n")).ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task The_requested_runway_is_the_one_in_use()
    {
        var result = await RunAsync(Atc(runway: "33"), "PRABC, solicito táxi\nsair\n");

        result.Output.ShouldContain("Pista em uso: 33.");
        result.Output.ShouldContain("PISTA TRÊS TRÊS.");
    }

    [Fact]
    public async Task An_unknown_airport_is_a_usage_error()
    {
        var result = await RunAsync(Atc("SBZZ"), "sair\n");

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("'SBZZ' não encontrado");
        result.Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_runway_the_airport_does_not_have_is_a_usage_error_that_lists_the_runways()
    {
        var result = await RunAsync(Atc(runway: "09"), "sair\n");

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("15/33");
        result.Error.ShouldContain("11/29");
    }

    [Fact]
    public async Task The_callsign_comes_from_the_simulator_when_it_is_not_given()
    {
        var asked = 0;

        var result = await RunAsync(
            Atc(callsign: null),
            "solicito acionamento, ABC\nsair\n",
            callsignFromSim: (_, _) =>
            {
                asked++;
                return Task.FromResult<string?>("pr-abc");
            });

        asked.ShouldBe(1);
        result.Output.ShouldContain("Aeronave PRABC");
        result.Output.ShouldContain("acionamento aprovado");
    }

    [Fact]
    public async Task The_simulator_is_not_asked_when_the_callsign_is_given()
    {
        var asked = 0;

        await RunAsync(Atc(), "sair\n", callsignFromSim: (_, _) =>
        {
            asked++;
            return Task.FromResult<string?>("XX-XXX");
        });

        asked.ShouldBe(0);
    }

    [Fact]
    public async Task Without_a_callsign_from_anywhere_it_fails_and_says_how_to_fix_it()
    {
        var result = await RunAsync(Atc(callsign: null), "sair\n");

        result.ExitCode.ShouldBe(1);
        result.Error.ShouldContain("--callsign");
    }

    [Fact]
    public async Task An_afis_airport_only_informs()
    {
        var afis = Airport("SBXX", "Marte", [AirportFrequencyType.Afis], Runway("11", "29", 4000));

        var result = await RunAsync(Atc(), "solicito acionamento, PRABC\nsair\n", afis);

        result.Output.ShouldContain("Responde: RÁDIO Marte.");
        result.Output.ShouldContain("RÁDIO Marte: PAPA ROMEO ALFA BRAVO CHARLIE, RÁDIO Marte, sem serviço de controle");
    }

    [Fact]
    public async Task An_airport_without_service_shows_a_notice_and_no_radio_speech()
    {
        var nobody = Airport("SBXX", "Marte", [], Runway("11", "29", 4000));

        var result = await RunAsync(Atc(), "solicito acionamento, PRABC\nsair\n", nobody);

        result.Output.ShouldContain("Sem serviço de controle neste aeródromo");
        result.Output.ShouldContain("[aviso] Sem serviço de controle em SBXX");
        result.Output.ShouldNotContain("PAPA ROMEO");
    }

    [Fact]
    public async Task Cancelling_ends_the_session_normally()
    {
        using var cancellation = new CancellationTokenSource();
        var input = new BlockingReader(cancellation);

        var console = new AtcConsole(
            new FakeCatalog(Solo),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            input,
            new StringWriter(),
            new StringWriter(),
            (_, _) => Task.FromResult<string?>(null),
            interactive: false);

        (await console.RunAsync(Atc(), cancellation.Token)).ShouldBe(0);
    }

    // A keyboard nobody types on: the read ends only because the session is cancelled.
    private sealed class BlockingReader(CancellationTokenSource cancellation) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }

    private sealed class FakeVoiceMode(int exitCode = 0) : IVoiceMode
    {
        public List<(AirportContext Airport, FlightContext Flight)> Calls { get; } = [];

        public Task<int> RunAsync(AirportContext airport, FlightContext flight, CancellationToken ct)
        {
            Calls.Add((airport, flight));
            return Task.FromResult(exitCode);
        }
    }

    [Fact]
    public async Task In_voice_mode_the_console_hands_over_to_the_voice_with_the_airport_and_the_flight()
    {
        var voice = new FakeVoiceMode();
        var output = new StringWriter { NewLine = "\n" };
        var console = new AtcConsole(
            new FakeCatalog(Solo),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new StringReader("isto nunca deve ser lido"),
            output,
            new StringWriter(),
            (_, _) => Task.FromResult<string?>(null),
            interactive: false,
            voiceMode: voice);

        var code = await console.RunAsync(Atc(), Ct);

        code.ShouldBe(0);
        var call = voice.Calls.ShouldHaveSingleItem();
        call.Airport.Airport.Ident.ShouldBe("SBXX");
        call.Flight.Callsign.ShouldBe("PRABC");
        call.Flight.Phase.ShouldBe(FlightPhase.Parked);
        output.ToString().ShouldContain("Responde: SOLO Curitiba");
        output.ToString().ShouldNotContain("Digite a fala do piloto");
    }

    [Fact]
    public async Task The_exit_code_of_the_voice_mode_is_the_exit_code_of_the_console()
    {
        var console = new AtcConsole(
            new FakeCatalog(Solo),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new StringReader(string.Empty),
            new StringWriter(),
            new StringWriter(),
            (_, _) => Task.FromResult<string?>(null),
            interactive: false,
            voiceMode: new FakeVoiceMode(exitCode: 1));

        (await console.RunAsync(Atc(), Ct)).ShouldBe(1);
    }
}
