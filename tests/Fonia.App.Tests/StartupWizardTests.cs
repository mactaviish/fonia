using System.Diagnostics.CodeAnalysis;
using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.App.Tests;

public class StartupWizardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeCatalog : IAirportCatalog
    {
        public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport found)
        {
            found = string.Equals(ident, "SBCT", StringComparison.OrdinalIgnoreCase)
                ? new Airport("SBCT", "Curitiba", "Curitiba", AirportType.Large, 0, 0, null, "SBCT", null, "Curitiba", SpokenNameSource.Reference, true, [], [])
                : null;

            return found is not null;
        }
    }

    private static async Task<(AppOptions? Options, string Output, string? Remembered)> RunAsync(string typed, string? defaultAirport = null)
    {
        var output = new StringWriter(new StringBuilder()) { NewLine = "\n" };
        string? remembered = null;

        var options = await StartupWizard.RunAsync(new StringReader(typed), output, new FakeCatalog(), defaultAirport, icao => remembered = icao, Ct);

        return (options, output.ToString(), remembered);
    }

    [Fact]
    public async Task The_airport_that_is_typed_starts_the_voice_mode_and_is_remembered()
    {
        var (options, _, remembered) = await RunAsync("sbct\n");

        options.ShouldBe(new AppOptions(AtcAirport: "SBCT", Voice: true));
        remembered.ShouldBe("SBCT");
    }

    [Fact]
    public async Task An_empty_answer_takes_the_default_airport_and_the_prompt_shows_it()
    {
        var (options, output, _) = await RunAsync("\n", defaultAirport: "SBCT");

        options!.AtcAirport.ShouldBe("SBCT");
        output.ShouldContain("[SBCT]");
    }

    [Fact]
    public async Task An_airport_that_is_not_in_the_data_is_asked_again()
    {
        var (options, output, _) = await RunAsync("XXXX\nSBCT\n");

        options!.AtcAirport.ShouldBe("SBCT");
        output.ShouldContain("XXXX");
        output.ShouldContain("não encontrado");
    }

    [Fact]
    public async Task Without_a_default_an_empty_answer_asks_again_and_giving_up_ends_it()
    {
        (await RunAsync("\n\n\n")).Options.ShouldBeNull();
        (await RunAsync("sair\n")).Options.ShouldBeNull();
        (await RunAsync(string.Empty)).Options.ShouldBeNull();
    }

    [Fact]
    public async Task The_last_airport_is_kept_on_disk_between_sessions()
    {
        var path = Path.Combine(Path.GetTempPath(), "fonia-last-" + Guid.NewGuid().ToString("N"), "last-airport.txt");

        try
        {
            var store = new LastAirportStore(path);

            store.Read().ShouldBeNull();
            store.Remember("SBCT");
            new LastAirportStore(path).Read().ShouldBe("SBCT");
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
            var store = new LastAirportStore(Path.Combine(blocker, "last-airport.txt"));

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
