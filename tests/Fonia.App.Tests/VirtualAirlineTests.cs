using System.Diagnostics.CodeAnalysis;
using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.App.Tests;

// A virtual airline: the callsign is free (WVR1234), the registration goes apart, and the telephony is the user's.
public class VirtualAirlineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AppOptions Ok(params string[] args)
    {
        var parsed = ArgumentParser.Parse(args);

        parsed.Error.ShouldBeNull();
        return parsed.Options.ShouldNotBeNull();
    }

    private static string Error(params string[] args) => ArgumentParser.Parse(args).Error.ShouldNotBeNull();

    [Fact]
    public void The_registration_and_the_telephony_are_options_of_the_atc_mode()
    {
        var options = Ok("--atc", "SBXX", "--callsign", "WVR1234", "--registration", "PR-ABC", "--telephony", "Wave Air");

        options.Callsign.ShouldBe("WVR1234");
        options.Registration.ShouldBe("PR-ABC");
        options.Telephony.ShouldNotBeNull().ShouldBe(new Dictionary<string, string> { ["WVR"] = "Wave Air" });
    }

    [Fact]
    public void The_telephony_can_name_its_designator()
    {
        Ok("--atc", "SBXX", "--callsign", "PRABC", "--telephony", "WVR=Wave Air").Telephony.ShouldNotBeNull()
            .ShouldBe(new Dictionary<string, string> { ["WVR"] = "Wave Air" });
        Ok("--atc", "SBXX", "--telephony=wvr=Wave Air", "--registration", "PRABC").Telephony.ShouldNotBeNull()
            .ShouldBe(new Dictionary<string, string> { ["WVR"] = "Wave Air" });
    }

    [Fact]
    public void Without_them_nothing_changes()
    {
        var options = Ok("--atc", "SBXX", "--callsign", "PRABC");

        options.Registration.ShouldBeNull();
        options.Telephony.ShouldBeNull();
    }

    [Fact]
    public void A_telephony_without_a_designator_needs_the_callsign_to_take_it_from()
    {
        Error("--atc", "SBXX", "--registration", "PRABC", "--telephony", "Wave Air").ShouldContain("--callsign");
        Error("--atc", "SBXX", "--callsign", "WV", "--telephony", "Wave Air").ShouldContain("--callsign");
    }

    [Fact]
    public void A_designator_that_is_not_three_letters_is_refused()
    {
        Error("--atc", "SBXX", "--callsign", "WVR1234", "--telephony", "WV=Wave Air").ShouldContain("--telephony");
        Error("--atc", "SBXX", "--callsign", "WVR1234", "--telephony", "W1R=Wave Air").ShouldContain("--telephony");
    }

    [Fact]
    public void A_registration_that_is_not_a_callsign_is_refused()
    {
        Error("--atc", "SBXX", "--registration", "A").ShouldContain("--registration");
    }

    [Fact]
    public void They_only_make_sense_in_the_atc_mode()
    {
        Error("--registration", "PR-ABC").ShouldContain("--atc");
        Error("--telephony", "WVR=Wave Air").ShouldContain("--atc");
    }

    [Fact]
    public void A_registration_alone_makes_the_simulator_option_pointless_like_a_callsign()
    {
        Error("--atc", "SBXX", "--registration", "PRABC", "--sim", "mock").ShouldContain("--registration dispensa o simulador");
        Ok("--atc", "SBXX", "--callsign", "WVR1234", "--registration", "PRABC").Source.ShouldBe(SimSourceKind.Msfs);
    }

    [Fact]
    public void The_settings_give_the_registration_and_the_companies_and_the_command_line_wins()
    {
        var settings = new AppSettings { Registration = "PR-XYZ", Telephony = { ["wvr"] = "Wave Air", ["ABC"] = "Alpha Bravo" } };

        var applied = SettingsApplier.Apply(new AppOptions(AtcAirport: "SBXX"), settings, new HashSet<string>());

        applied.Error.ShouldBeNull();
        applied.Options.Registration.ShouldBe("PR-XYZ");
        applied.Options.Telephony.ShouldNotBeNull().ShouldBe(new Dictionary<string, string> { ["WVR"] = "Wave Air", ["ABC"] = "Alpha Bravo" }, ignoreOrder: true);

        var typed = new AppOptions(AtcAirport: "SBXX", Registration: "PR-ABC", Telephony: new Dictionary<string, string> { ["WVR"] = "Wave Airlines" });
        var overridden = SettingsApplier.Apply(typed, settings, new HashSet<string>(["registration"], StringComparer.OrdinalIgnoreCase));

        overridden.Options.Registration.ShouldBe("PR-ABC");
        overridden.Options.Telephony.ShouldNotBeNull()["WVR"].ShouldBe("Wave Airlines");
        overridden.Options.Telephony["ABC"].ShouldBe("Alpha Bravo");
    }

    [Fact]
    public void A_wrong_company_in_the_settings_is_reported_with_its_key()
    {
        var applied = SettingsApplier.Apply(new AppOptions(AtcAirport: "SBXX"), new AppSettings { Telephony = { ["WV"] = "Wave Air" } }, new HashSet<string>());

        applied.Error.ShouldNotBeNull().ShouldContain("telephony");
        SettingsApplier.Apply(new AppOptions(AtcAirport: "SBXX"), new AppSettings { Registration = "x" }, new HashSet<string>()).Error.ShouldNotBeNull().ShouldContain("registration");
    }

    [Fact]
    public void The_table_of_the_run_has_the_companies_added_and_the_ones_of_the_repository()
    {
        var table = Companies.From(new AppOptions(Telephony: new Dictionary<string, string> { ["WVR"] = "Wave Air" }));

        SpokenText.Callsign("WVR1234", telephony: table).ShouldBe("WAVE AIR UNO DOIS TRÊS QUATRO");
        SpokenText.Callsign("GLO1234", telephony: table).ShouldBe("GOL UNO DOIS TRÊS QUATRO");
        SpokenText.Callsign("WVR1234", telephony: Companies.From(new AppOptions())).ShouldStartWith("WHISKEY");
    }

    private sealed class FakeCatalog(Airport airport) : IAirportCatalog
    {
        public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport found)
        {
            found = string.Equals(airport.Ident, ident, StringComparison.OrdinalIgnoreCase) ? airport : null;
            return found is not null;
        }
    }

    private static readonly Airport Apron = new(
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
        [new Runway(new RunwayEnd("15", 150, null, null), new RunwayEnd("33", 330, null, null), 6000, 150, "ASP", true, false)],
        [new AirportFrequency(AirportFrequencyType.Ground, new Frequency(118000), "Ground"), new AirportFrequency(AirportFrequencyType.Tower, new Frequency(119500), "Tower")]);

    private static async Task<(string Output, string Error)> RunAsync(AppOptions options, string typed)
    {
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var error = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var companies = Companies.From(options);

        var console = new AtcConsole(
            new FakeCatalog(Apron),
            new RuleBasedNlu(companies),
            new AtcEngine(),
            new PortuguesePhraseology(companies),
            new StringReader(typed),
            output,
            error,
            (_, _) => Task.FromResult<string?>(null),
            interactive: false);

        await console.RunAsync(options, Ct);

        return (output.ToString(), error.ToString());
    }

    private static readonly Dictionary<string, string> Wave = new() { ["WVR"] = "Wave Air" };

    [Fact]
    public async Task The_flight_of_a_virtual_airline_is_said_and_understood_by_its_telephony()
    {
        var options = new AppOptions(AtcAirport: "SBXX", Callsign: "WVR1234", Registration: "PR-ABC", Telephony: Wave);

        var (output, error) = await RunAsync(options, "Solo Curitiba, wave air um dois três quatro, solicito acionamento\nsair\n");

        error.ShouldBeEmpty();
        output.ShouldContain("Aeronave WVR1234 (matrícula PRABC).");
        output.ShouldContain("SOLO Curitiba: WAVE AIR UNO DOIS TRÊS QUATRO, acionamento aprovado");
    }

    [Fact]
    public async Task Without_the_telephony_the_flight_is_spelled_and_still_understood_that_way()
    {
        var (output, _) = await RunAsync(new AppOptions(AtcAirport: "SBXX", Callsign: "WVR1234"), "Solo Curitiba, whiskey victor romeo um dois três quatro, solicito acionamento\nsair\n");

        output.ShouldContain("Aeronave WVR1234.");
        output.ShouldContain("SOLO Curitiba: WHISKEY VICTOR ROMEO UNO DOIS TRÊS QUATRO, acionamento aprovado");
    }

    [Fact]
    public async Task The_registration_is_the_callsign_when_there_is_no_other_and_the_simulator_is_not_asked()
    {
        var asked = false;
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        var console = new AtcConsole(
            new FakeCatalog(Apron),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new StringReader("sair\n"),
            output,
            new StringWriter(),
            (_, _) =>
            {
                asked = true;
                return Task.FromResult<string?>("PRSIM");
            },
            interactive: false);

        await console.RunAsync(new AppOptions(AtcAirport: "SBXX", Registration: "PR-ABC"), Ct);

        asked.ShouldBeFalse();
        output.ToString().ShouldContain("Aeronave PRABC.");
    }

    [Fact]
    public void The_airline_the_simulator_gives_is_a_flight_when_the_user_added_its_telephony()
    {
        var state = new AircraftState(DateTimeOffset.UtcNow, -25.5, -49.1, 3000, 0, 0, 110, true, null, null, 1200, "PR-ABC", "B738", "Wave Air", "1234");
        var table = Companies.From(new AppOptions(Telephony: Wave));

        state.Callsign.ShouldBe("PR-ABC");
        state.CallsignWith(table).ShouldBe("WVR1234");
    }
}
