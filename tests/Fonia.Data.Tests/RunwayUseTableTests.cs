using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Atc;

namespace Fonia.Data.Tests;

public class RunwayUseTableTests
{
    private static RunwayUseTable From(string csv) => RunwayUseTable.Create(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

    [Fact]
    public void The_embedded_file_has_the_runways_of_guarulhos_for_both_directions()
    {
        var table = RunwayUseTable.Load();

        table.Warnings.ShouldBeEmpty();
        table.RulesFor("SBGR").ShouldBe([new RunwayUseRule("10R", "10L"), new RunwayUseRule("28L", "28R")]);
    }

    [Fact]
    public void The_code_of_the_airport_may_be_written_in_any_case_and_an_unknown_one_has_no_rules()
    {
        var table = RunwayUseTable.Load();

        table.RulesFor("sbgr").Count.ShouldBe(2);
        table.RulesFor(" SBGR ").Count.ShouldBe(2);
        table.RulesFor("SBCT").ShouldBeEmpty();
        table.RulesFor(null).ShouldBeEmpty();
        table.RulesFor(string.Empty).ShouldBeEmpty();
    }

    // The file is the maintainer's word about how an airport works: it must agree with the runways the airport data has.
    [Fact]
    public void The_runways_of_the_embedded_rules_are_runways_the_airports_have()
    {
        var catalog = AirportCatalog.Load();

        catalog.TryGet("SBGR", out var guarulhos).ShouldBeTrue();

        var ends = guarulhos.Runways.Where(r => !r.Closed).SelectMany(r => r.Ends).Select(e => RunwayDesignator.Normalize(e.Designator)).ToHashSet(StringComparer.Ordinal);

        foreach (var rule in RunwayUseTable.Load().RulesFor("SBGR"))
        {
            ends.ShouldContain(rule.Arrival);
            ends.ShouldContain(rule.Departure);
        }
    }

    [Fact]
    public void Used_on_the_real_airport_the_rules_split_the_runways_of_guarulhos()
    {
        AirportCatalog.Load().TryGet("SBGR", out var guarulhos).ShouldBeTrue();
        var rules = RunwayUseTable.Load().RulesFor("SBGR");

        var tenth = AirportContext.Create(guarulhos, "10L").WithRunwayUse(rules, keepRunway: false);
        var twentyEighth = AirportContext.Create(guarulhos, "28R").WithRunwayUse(rules, keepRunway: false);

        (tenth.RunwayInUse, tenth.RunwayForArrival).ShouldBe(("10L", "10R"));
        (twentyEighth.RunwayInUse, twentyEighth.RunwayForArrival).ShouldBe(("28R", "28L"));
    }

    [Fact]
    public void A_row_that_is_not_usable_is_skipped_with_a_warning_and_the_others_are_kept()
    {
        var table = From(
            """
            icao,arrival,departure,source,notes
            SBXX,09R,09L,mantenedor,ok
            ,09R,09L,mantenedor,no icao
            SBYY,banana,09L,mantenedor,not a runway
            SBZZ,09L,09L,mantenedor,the same runway
            SBXX,27L,27R,mantenedor,ok too
            """);

        table.RulesFor("SBXX").ShouldBe([new RunwayUseRule("09R", "09L"), new RunwayUseRule("27L", "27R")]);
        table.RulesFor("SBYY").ShouldBeEmpty();
        table.RulesFor("SBZZ").ShouldBeEmpty();
        table.Warnings.Count.ShouldBe(3);
        table.Warnings.ShouldContain(w => w.StartsWith("line 3:", StringComparison.Ordinal));
        table.Warnings.ShouldContain(w => w.Contains("same runway", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_with_only_the_header_has_no_rules_and_no_warnings()
    {
        var table = From("icao,arrival,departure,source,notes\n");

        table.RulesFor("SBGR").ShouldBeEmpty();
        table.Warnings.ShouldBeEmpty();
    }
}
