using System.Diagnostics.CodeAnalysis;
using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Weather;

namespace Fonia.App.Tests;

public class AtcConsoleFrequencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeCatalog(Airport airport) : IAirportCatalog
    {
        public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport found)
        {
            found = airport;
            return string.Equals(ident, airport.Ident, StringComparison.OrdinalIgnoreCase);
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

    // Solo 118.0 and Torre 119.5, runway 11/29 (the longer) in use as 11.
    private static readonly Airport Curitiba = new(
        "SBXX",
        "Curitiba Airport",
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
        [new AirportFrequency(AirportFrequencyType.Ground, new Frequency(118000), "Ground"), new AirportFrequency(AirportFrequencyType.Tower, new Frequency(119500), "Tower")]);

    private static async Task<(int Code, string Output, string Error)> RunAsync(AppOptions options, string typed, Func<Frequency?>? live = null, IMetarSource? metar = null, Func<AirportContext, string, CaseLog?>? cases = null)
    {
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var error = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var console = new AtcConsole(
            new FakeCatalog(Curitiba),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new StringReader(typed),
            output,
            error,
            (_, _) => Task.FromResult<string?>(null),
            interactive: false,
            liveCom1: live,
            timeProvider: new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 14, 12, 26, 31, TimeSpan.Zero)),
            metarSource: metar,
            cases: cases);

        var code = await console.RunAsync(options, Ct);

        return (code, output.ToString(), error.ToString());
    }

    private static AppOptions Atc(Frequency? com1 = null) => new(AtcAirport: "SBXX", Callsign: "PR-ABC", Com1: com1);

    [Fact]
    public async Task The_header_says_who_answers_on_the_radio_that_was_set()
    {
        var result = await RunAsync(Atc(new Frequency(119500)), "sair\n");

        result.Output.ShouldContain("COM1 119.5: TORRE Curitiba atende.");
    }

    [Fact]
    public async Task Without_a_radio_the_header_does_not_mention_it_and_the_airports_own_unit_answers()
    {
        var result = await RunAsync(Atc(), "Solo Curitiba, PRABC, solicito acionamento\nsair\n");

        result.Output.ShouldNotContain("COM1 ", Case.Sensitive);
        result.Output.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado");
    }

    [Fact]
    public async Task On_the_torres_frequency_an_engine_start_gets_the_notice_to_call_the_solo_not_the_solos_answer()
    {
        var result = await RunAsync(Atc(new Frequency(119500)), "PRABC, solicito acionamento\nsair\n");

        result.Output.ShouldContain("[aviso] TORRE Curitiba: a Torre não trata de acionamento");
        result.Output.ShouldNotContain("acionamento aprovado");
    }

    [Fact]
    public async Task The_radio_can_be_changed_during_the_session_and_the_answer_follows()
    {
        var typed = "/com1 119.5\nPRABC, solicito acionamento\n/com1 118\nPRABC, solicito acionamento\nsair\n";

        var result = await RunAsync(Atc(), typed);

        result.Output.ShouldContain("COM1 119.5: TORRE Curitiba atende.");
        result.Output.ShouldContain("COM1 118.0: SOLO Curitiba atende.");
        result.Output.ShouldContain("[aviso] TORRE Curitiba");
        result.Output.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado");
    }

    [Theory]
    [InlineData("/com1 118,0")]
    [InlineData("/COM1 118.0")]
    [InlineData("  /com1   118.0  ")]
    public async Task The_command_takes_a_comma_or_a_dot_and_any_case(string command)
    {
        var result = await RunAsync(Atc(), command + "\nsair\n");

        result.Output.ShouldContain("COM1 118.0: SOLO Curitiba atende.");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task On_a_frequency_of_nobody_the_console_says_so_and_which_frequencies_exist()
    {
        var result = await RunAsync(Atc(), "/com1 121.5\nsair\n");

        result.Output.ShouldContain("Ninguém responde em 121.5");
        result.Output.ShouldContain("Solo 118");
        result.Output.ShouldContain("Torre 119.5");
    }

    [Fact]
    public async Task Without_a_value_the_command_shows_the_current_radio()
    {
        var result = await RunAsync(Atc(new Frequency(119500)), "/com1\nsair\n");

        result.Output.Split('\n').Count(l => l.Contains("COM1 119.5: TORRE Curitiba atende.", StringComparison.Ordinal)).ShouldBe(2, "the header and the answer to the command");
    }

    [Fact]
    public async Task Off_gives_the_radio_back_to_the_airports_own_unit()
    {
        var result = await RunAsync(Atc(new Frequency(119500)), "/com1 off\nPRABC, solicito acionamento\nsair\n");

        result.Output.ShouldContain("COM1 livre");
        result.Output.ShouldContain("acionamento aprovado");
    }

    [Theory]
    [InlineData("/com1 abc")]
    [InlineData("/com1 50")]
    [InlineData("/com1 118.5 118.6")]
    public async Task A_bad_frequency_is_refused_and_the_radio_stays_as_it_was(string command)
    {
        var result = await RunAsync(Atc(new Frequency(118000)), command + "\nPRABC, solicito acionamento\nsair\n");

        result.Error.ShouldContain("frequência inválida");
        result.Output.ShouldContain("acionamento aprovado");
    }

    [Fact]
    public async Task A_radio_read_from_the_simulator_is_followed_at_every_line()
    {
        Frequency? now = new Frequency(118000);
        var live = () => now;
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var input = new BlockingReader(["PRABC, solicito acionamento", "PRABC, solicito acionamento"], () => now = new Frequency(119500));
        var console = new AtcConsole(
            new FakeCatalog(Curitiba), new RuleBasedNlu(), new AtcEngine(), new PortuguesePhraseology(), input, output, new StringWriter(),
            (_, _) => Task.FromResult<string?>(null), interactive: false, liveCom1: live);

        await console.RunAsync(Atc(), Ct);

        var text = output.ToString();
        text.IndexOf("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado", StringComparison.Ordinal).ShouldBeGreaterThan(-1);
        text.IndexOf("[aviso] TORRE Curitiba", StringComparison.Ordinal).ShouldBeGreaterThan(text.IndexOf("acionamento aprovado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_the_user_typed_wins_over_the_simulator()
    {
        var result = await RunAsync(Atc(new Frequency(118000)), "PRABC, solicito acionamento\nsair\n", live: () => new Frequency(119500));

        result.Output.ShouldContain("acionamento aprovado");
    }

    [Fact]
    public async Task The_whole_way_from_the_apron_to_the_handoff()
    {
        var typed = string.Join(
            '\n',
            "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento",
            "acionamento aprovado, Papa Romeo Alfa Bravo Charlie",
            "Papa Romeo Alfa Bravo Charlie, pronto para o táxi",
            "autorizado táxi pista uno uno, Papa Romeo Alfa Bravo Charlie",
            "Papa Romeo Alfa Bravo Charlie, no ponto de espera da pista uno uno",
            "Uno noito, decimal cinco, cinco, Alfa Bravo Charlie.",
            "sair\n");

        var result = await RunAsync(Atc(), typed);

        result.Output.ShouldContain("autorizado táxi para o ponto de espera da PISTA UNO UNO.");
        result.Output.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, chame Torre Curitiba UNO UNO NOVE DECIMAL CINCO.");
        result.Output.ShouldNotContain("Repita");
        result.Output.ShouldContain("(o controlador não responde)");
    }

    [Fact]
    public async Task The_whole_way_from_the_apron_to_the_takeoff_clearance_on_two_frequencies()
    {
        var typed = string.Join(
            '\n',
            "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento",
            "acionamento aprovado, Papa Romeo Alfa Bravo Charlie",
            "Papa Romeo Alfa Bravo Charlie, pronto para o táxi",
            "autorizado táxi pista uno uno, Papa Romeo Alfa Bravo Charlie",
            "Papa Romeo Alfa Bravo Charlie, no ponto de espera da pista uno uno",
            "Chamando Torre Curitiba, uno uno nove decimal cinco, Alfa Bravo Charlie",
            "/com1 119.5",
            "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, ponto de espera pista uno uno, pronto para partida",
            "alinhe e mantenha pista uno uno, Papa Romeo Alfa Bravo Charlie",
            "Papa Romeo Alfa Bravo Charlie, alinhado",
            "autorizado decolagem pista uno uno, Papa Romeo Alfa Bravo Charlie",
            "Papa Romeo Alfa Bravo Charlie, decolando",
            "sair\n");

        var result = await RunAsync(Atc(), typed);

        var text = result.Output;
        text.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, chame Torre Curitiba UNO UNO NOVE DECIMAL CINCO.");
        text.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, PISTA UNO UNO, alinhe e mantenha.");
        text.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, PISTA UNO UNO, autorizado decolagem.");
        text.ShouldNotContain("repita");
        text.ShouldNotContain("negativo");
        result.Error.ShouldBeEmpty();
        text.Split('\n').Count(l => l.Contains("(o controlador não responde)", StringComparison.Ordinal)).ShouldBe(6, "five readbacks (startup, taxi, handoff, line up, takeoff) and the takeoff roll are taken in silence");
    }

    [Fact]
    public async Task A_wrong_readback_of_the_line_up_is_corrected_by_the_torre()
    {
        var typed = string.Join(
            '\n',
            "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento",
            "Papa Romeo Alfa Bravo Charlie, pronto para o táxi",
            "Papa Romeo Alfa Bravo Charlie, no ponto de espera da pista uno uno",
            "/com1 119.5",
            "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, pronto para decolagem",
            "alinhe e mantenha pista uno cinco, Papa Romeo Alfa Bravo Charlie",
            "sair\n");

        var result = await RunAsync(Atc(), typed);

        result.Output.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, negativo, PISTA UNO UNO, alinhe e mantenha.");
    }

    private const string ToTheTakeoffClearance =
        "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento\n" +
        "Papa Romeo Alfa Bravo Charlie, pronto para o táxi\n" +
        "Papa Romeo Alfa Bravo Charlie, no ponto de espera da pista uno uno\n" +
        "/com1 119.5\n" +
        "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, pronto para decolagem\n" +
        "Papa Romeo Alfa Bravo Charlie, alinhado\n";

    [Fact]
    public async Task The_aircraft_leaving_the_ground_after_the_clearance_is_told_by_the_torre_with_the_minute()
    {
        var result = await RunAsync(Atc(), ToTheTakeoffClearance + "/evento decolou\nsair\n");

        result.Output.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, decolado aos DOIS MEIA.");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_takeoff_nobody_cleared_gets_no_word_from_the_controller()
    {
        var result = await RunAsync(Atc(), "/evento decolou\nsair\n");

        result.Output.ShouldContain("(o controlador não diz nada sobre isso)");
        result.Output.ShouldNotContain("decolado aos");
    }

    [Fact]
    public async Task An_unknown_event_is_refused_with_the_one_that_exists()
    {
        var result = await RunAsync(Atc(), "/evento voou\nsair\n");

        result.Error.ShouldContain("/evento decolou ou /evento pousou");
    }

    [Fact]
    public async Task The_whole_arrival_from_the_circuit_to_the_landing_clearance_on_the_torre()
    {
        var typed = string.Join(
            '\n',
            "/com1 119.5",
            "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, cinco milhas ao sul, instruções para pouso",
            "autorizado circuito de tráfego pista uno uno, Papa Romeo Alfa Bravo Charlie",
            "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, perna do vento",
            "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, perna base",
            "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, final",
            "pista uno uno, pouso autorizado, Papa Romeo Alfa Bravo Charlie",
            "sair\n");

        var result = await RunAsync(Atc(), typed);

        var text = result.Output;
        text.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, autorizado para o circuito de tráfego, PISTA UNO UNO, reporte perna do vento.");
        text.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, reporte perna base.");
        text.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, reporte na final.");
        text.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, PISTA UNO UNO, pouso autorizado.");
        text.ShouldNotContain("repita");
        text.ShouldNotContain("negativo");
        result.Error.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_solo_asked_to_land_says_to_call_the_torre()
    {
        var result = await RunAsync(Atc(), "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, instruções para pouso\nsair\n");

        result.Output.ShouldContain("[aviso] SOLO Curitiba: o Solo não trata de chegada neste app: chame a Torre em 119.5.");
    }

    [Fact]
    public async Task After_the_landing_the_torre_says_the_minute_and_the_solo_and_the_apron_follows()
    {
        var typed = string.Join(
            '\n',
            "/com1 119.5",
            "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, instruções para pouso",
            "autorizado circuito de tráfego pista uno uno, Papa Romeo Alfa Bravo Charlie",
            "Torre Curitiba, Papa Romeo Alfa Bravo Charlie, final",
            "pista uno uno, pouso autorizado, Papa Romeo Alfa Bravo Charlie",
            "/evento pousou",
            "ciente, chamo o Solo, Papa Romeo Alfa Bravo Charlie",
            "/com1 118.0",
            "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, pista livre",
            "taxi para o pátio, Papa Romeo Alfa Bravo Charlie",
            "sair\n");

        var result = await RunAsync(Atc(), typed);

        var text = result.Output;
        text.ShouldContain("TORRE Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, no solo aos DOIS MEIA, após livrar a pista chame Solo Curitiba UNO UNO OITO DECIMAL ZERO.");
        text.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, taxie para o pátio.");
        text.ShouldNotContain("repita");
        text.ShouldNotContain("negativo");
        result.Error.ShouldBeEmpty();
    }

    private sealed class FakeMetar(MetarReading? reading) : IMetarSource
    {
        public int Calls { get; private set; }

        public Task<MetarReading?> GetAsync(string icao, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(reading);
        }
    }

    private static MetarReading Weather(string raw, bool stale = false)
    {
        MetarParser.TryParse(raw, out var metar).ShouldBeTrue();
        return new MetarReading(metar, new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero), stale);
    }

    [Fact]
    public async Task The_metar_is_shown_and_the_runway_in_use_is_the_one_the_wind_favours()
    {
        var metar = new FakeMetar(Weather("SBXX 141200Z 28012KT 9999 FEW030 22/14 Q1018"));

        var result = await RunAsync(Atc(), "sair\n", metar: metar);

        result.Output.ShouldContain("SBXX 141200Z 28012KT 9999 FEW030 22/14 Q1018 (observado às 12:00Z, há 26 min).");
        result.Output.ShouldContain("Pista em uso: 29.");
    }

    [Fact]
    public async Task The_clearances_say_the_wind_and_the_altimeter_setting_from_the_metar()
    {
        var typed = string.Join(
            '\n',
            "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento",
            "Papa Romeo Alfa Bravo Charlie, pronto para o táxi",
            "sair\n");

        var result = await RunAsync(Atc(), typed, metar: new FakeMetar(Weather("SBXX 141200Z 28012KT 9999 FEW030 22/14 Q1018")));

        result.Output.ShouldContain("autorizado táxi para o ponto de espera da PISTA DOIS NOVE, ajuste de altímetro UNO ZERO UNO OITO.");
    }

    [Fact]
    public async Task A_runway_the_user_asked_for_is_kept_over_the_wind()
    {
        var options = Atc() with { Runway = "11" };

        var result = await RunAsync(options, "sair\n", metar: new FakeMetar(Weather("SBXX 141200Z 28012KT 9999 22/14 Q1018")));

        result.Output.ShouldContain("Pista em uso: 11.");
    }

    [Fact]
    public async Task A_metar_from_the_cache_says_so()
    {
        var result = await RunAsync(Atc(), "sair\n", metar: new FakeMetar(Weather("SBXX 141200Z 28012KT 9999 22/14 Q1018", stale: true)));

        result.Output.ShouldContain("guardado: sem rede");
    }

    [Fact]
    public async Task Without_a_metar_the_radio_works_and_the_user_is_told_why_the_wind_is_missing()
    {
        var result = await RunAsync(Atc(), "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, pronto para o táxi\nsair\n", metar: new FakeMetar(null));

        result.Output.ShouldContain("Sem METAR");
        result.Output.ShouldContain("autorizado táxi para o ponto de espera da PISTA UNO UNO.");
        result.Output.ShouldNotContain("altímetro UNO");
    }

    [Fact]
    public async Task With_no_metar_the_source_is_not_even_asked()
    {
        var metar = new FakeMetar(Weather("SBXX 141200Z 28012KT 9999 22/14 Q1018"));

        var result = await RunAsync(Atc() with { NoMetar = true }, "sair\n", metar: metar);

        metar.Calls.ShouldBe(0);
        result.Output.ShouldNotContain("METAR");
        result.Output.ShouldNotContain("Sem METAR");
    }

    [Fact]
    public async Task The_pilot_who_asks_for_the_conditions_hears_the_runway_the_weather_and_the_time_check()
    {
        var metar = new FakeMetar(Weather("SBXX 141200Z 28012KT 9999 FEW030 22/14 Q1018"));

        var result = await RunAsync(Atc(), "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicita condições do aeródromo\nsair\n", metar: metar);

        result.Output.ShouldContain(
            "SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, PISTA DOIS NOVE, VENTO DOIS OITO ZERO GRAUS, UNO DOIS NÓS, ajuste de altímetro UNO ZERO UNO OITO, temperatura DOIS DOIS, visibilidade DEZ QUILÔMETROS OU MAIS, hora certa DOIS MEIA.");
        result.Output.ShouldNotContain("repita");
    }

    [Fact]
    public async Task Without_a_metar_the_conditions_are_the_runway_and_the_time_check()
    {
        var result = await RunAsync(Atc(), "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, informações de partida\nsair\n", metar: new FakeMetar(null));

        result.Output.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, PISTA UNO UNO, hora certa DOIS MEIA.");
    }

    // A reader that hands over its lines one at a time and runs an action between them, like a person at a keyboard.
    private sealed class BlockingReader(string[] lines, Action between) : TextReader
    {
        private int _next;

        public override string? ReadLine()
        {
            if (_next >= lines.Length)
            {
                return null;
            }

            if (_next == 1)
            {
                between();
            }

            return lines[_next++];
        }
    }

    private static Func<AirportContext, string, CaseLog?> CasesIn(string root, out Func<CaseLog?> made)
    {
        CaseLog? log = null;
        made = () => log;

        return (airport, callsign) => log = new CaseLog(root, TimeProvider.System, new PortuguesePhraseology(), airport, callsign, saveAudio: false);
    }

    [Fact]
    public async Task What_the_controller_could_not_follow_is_kept_and_a_slash_command_marks_a_mistake()
    {
        var root = Path.Combine(Path.GetTempPath(), "fonia-console-cases-" + Guid.NewGuid().ToString("N"));

        try
        {
            var cases = CasesIn(root, out var made);

            var result = await RunAsync(
                Atc(),
                "Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento\nblá blá blá\n/errado\n/errado\nsair\n",
                cases: cases);

            result.Output.ShouldContain("guardado para melhoria: nao-entendido");
            result.Output.ShouldContain("Troca marcada como errada");
            result.Output.ShouldContain("Nada para marcar ainda");
            made()!.Cases.ShouldBe(1);

            var lines = File.ReadAllLines(Path.Combine(made()!.SessionDirectory, "cases.jsonl"));
            lines.Length.ShouldBe(3, "session, case and mark");
            lines[1].ShouldContain("blá blá blá");
            lines[2].ShouldContain("\"type\":\"mark\"");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task Without_the_log_the_mark_command_says_why_nothing_was_kept()
    {
        var result = await RunAsync(Atc(), "/errado\nsair\n");

        result.Output.ShouldContain("--no-cases");
    }
}
