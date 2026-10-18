using System.Diagnostics.CodeAnalysis;
using System.Text;
using Fonia.Core.Airports;

namespace Fonia.App.Tests;

public class StartupWizardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Airport Curitiba { get; } =
        new("SBCT", "Curitiba", "Curitiba", AirportType.Large, -25.5285, -49.1758, 2988, "SBCT", null, "Curitiba", SpokenNameSource.Reference, true, [], []);

    private sealed class FakeCatalog : IAirportCatalog
    {
        public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport found)
        {
            found = string.Equals(ident, "SBCT", StringComparison.OrdinalIgnoreCase) ? Curitiba : null;

            return found is not null;
        }
    }

    private sealed record Run(AppOptions? Options, string Output, string? AirportRemembered, string? CallsignRemembered, int SimulatorLooks);

    // The simulator is there (a fix) or is not (null); the nearest airport is Curitiba at the given distance, or none.
    private static async Task<Run> RunAsync(string typed, SimulatorFix? fix = null, double? kmToCuritiba = 0.4, WizardDefaults? defaults = null)
    {
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        string? airport = null;
        string? callsign = null;
        var looks = 0;

        var options = await StartupWizard.RunAsync(
            new StringReader(typed),
            output,
            new FakeCatalog(),
            defaults ?? new WizardDefaults(null, null),
            _ =>
            {
                looks++;
                return Task.FromResult(fix);
            },
            (_, _) => kmToCuritiba is { } km ? (Curitiba, km) : null,
            icao => airport = icao,
            sign => callsign = sign,
            Ct);

        return new Run(options, output.ToString(), airport, callsign, looks);
    }

    private static readonly SimulatorFix OnTheApron = new("PR-FAB", -25.5285, -49.1758);

    [Fact]
    public async Task With_the_simulator_both_the_callsign_and_the_airport_come_from_it_and_nothing_is_asked()
    {
        var run = await RunAsync(string.Empty, OnTheApron);

        run.Options.ShouldBe(new AppOptions(AtcAirport: "SBCT", Voice: true, Callsign: "PR-FAB"));
        run.Output.ShouldContain("PR-FAB");
        run.Output.ShouldContain("SBCT");
        run.Output.ShouldNotContain("Aeroporto (");
        run.Output.ShouldNotContain("Indicativo (");
        run.AirportRemembered.ShouldBe("SBCT");
        run.CallsignRemembered.ShouldBe("PR-FAB");
    }

    [Fact]
    public async Task Without_the_simulator_both_are_asked_and_the_user_is_told_why()
    {
        var run = await RunAsync("sbct\nPR-ABC\n", fix: null);

        run.Options.ShouldBe(new AppOptions(AtcAirport: "SBCT", Voice: true, Callsign: "PR-ABC"));
        run.Output.ShouldContain("Simulador não encontrado");
        run.Output.ShouldContain("Aeroporto (");
        run.Output.ShouldContain("Indicativo (");
        run.AirportRemembered.ShouldBe("SBCT");
        run.CallsignRemembered.ShouldBe("PR-ABC");
        run.SimulatorLooks.ShouldBe(1);
    }

    [Fact]
    public async Task A_simulator_far_from_any_airport_still_gives_the_callsign_and_the_airport_is_asked()
    {
        var run = await RunAsync("SBCT\n", OnTheApron, kmToCuritiba: 120);

        run.Options.ShouldBe(new AppOptions(AtcAirport: "SBCT", Voice: true, Callsign: "PR-FAB"));
        run.Output.ShouldContain("nenhum aeroporto por perto");
        run.Output.ShouldNotContain("Indicativo (");
    }

    [Fact]
    public async Task A_simulator_with_no_callsign_set_has_the_callsign_asked()
    {
        var run = await RunAsync("PR-ABC\n", new SimulatorFix(string.Empty, OnTheApron.LatitudeDegrees, OnTheApron.LongitudeDegrees));

        run.Options.ShouldBe(new AppOptions(AtcAirport: "SBCT", Voice: true, Callsign: "PR-ABC"));
    }

    [Fact]
    public async Task The_defaults_are_offered_and_an_empty_answer_takes_them()
    {
        var run = await RunAsync("\n\n", fix: null, defaults: new WizardDefaults("SBCT", "PR-ABC"));

        run.Options.ShouldBe(new AppOptions(AtcAirport: "SBCT", Voice: true, Callsign: "PR-ABC"));
        run.Output.ShouldContain("[SBCT]");
        run.Output.ShouldContain("[PR-ABC]");
    }

    [Fact]
    public async Task An_airport_that_is_not_in_the_data_is_asked_again()
    {
        var run = await RunAsync("XXXX\nSBCT\nPR-ABC\n", fix: null);

        run.Options!.AtcAirport.ShouldBe("SBCT");
        run.Output.ShouldContain("XXXX");
        run.Output.ShouldContain("não encontrado");
    }

    [Theory]
    [InlineData("--\nx\n!!\n")]
    [InlineData("a\nb\nc\n")]
    public async Task A_callsign_that_is_not_one_is_asked_again_and_three_failures_end_it(string typed)
    {
        var run = await RunAsync(typed, OnTheApron with { Registration = string.Empty });

        run.Options.ShouldBeNull();
        run.Output.ShouldContain("indicativo");
    }

    [Theory]
    [InlineData("PR-ABC", "PRABC")]
    [InlineData("pr abc", "PRABC")]
    [InlineData(" PT-ABC ", "PTABC")]
    public async Task The_callsign_is_taken_with_or_without_the_dash_and_the_spaces(string typed, string normalized)
    {
        var run = await RunAsync($"SBCT\n{typed}\n", fix: null);

        StartupWizard.NormalizeCallsign(run.Options!.Callsign).ShouldBe(normalized);
    }

    [Fact]
    public async Task Without_a_default_an_empty_answer_asks_again_and_giving_up_ends_it()
    {
        (await RunAsync("\n\n\n", fix: null)).Options.ShouldBeNull();
        (await RunAsync("sair\n", fix: null)).Options.ShouldBeNull();
        (await RunAsync(string.Empty, fix: null)).Options.ShouldBeNull();
        (await RunAsync("SBCT\nsair\n", fix: null)).Options.ShouldBeNull();
    }

    [Fact]
    public void The_last_answers_are_kept_on_disk_between_sessions()
    {
        var path = Path.Combine(Path.GetTempPath(), "fonia-last-" + Guid.NewGuid().ToString("N"), "last-airport.txt");

        try
        {
            var store = new LastValueStore(path);

            store.Read().ShouldBeNull();
            store.Remember("SBCT");
            new LastValueStore(path).Read().ShouldBe("SBCT");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Directory.Delete(Path.GetDirectoryName(path)!);
            }
        }
    }

    [Fact]
    public void A_store_that_cannot_be_read_or_written_is_not_a_problem()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "fonia-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "x");

        try
        {
            var store = new LastValueStore(Path.Combine(blocker, "last-airport.txt"));

            Should.NotThrow(() => store.Remember("SBCT"));
            store.Read().ShouldBeNull();
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void Starting_without_arguments_is_for_a_terminal_only()
    {
        StartupWizard.ShouldAsk(args: [], terminal: true).ShouldBeTrue();
        StartupWizard.ShouldAsk(args: [], terminal: false).ShouldBeFalse();
        StartupWizard.ShouldAsk(args: ["--sim", "mock"], terminal: true).ShouldBeFalse();
    }
}
