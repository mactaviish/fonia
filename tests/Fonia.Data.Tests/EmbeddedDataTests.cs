using System.Diagnostics;
using Fonia.Core.Airports;
using Fonia.Data;

namespace Fonia.Data.Tests;

/// <summary>
/// The real files embedded in the assembly. They change when the data is refreshed (tools/update-ourairports.cs) or the
/// names file is edited, so these tests check what must hold for any good snapshot, not exact values: they are the guard
/// that a refresh or an edit did not break something.
/// </summary>
public class EmbeddedDataTests
{
    private static readonly Lazy<AirportCatalog> Real = new(AirportCatalog.Load);

    private static AirportCatalog Catalog => Real.Value;

    private static CsvTable NamesFile()
    {
        using var stream = typeof(AirportCatalog).Assembly.GetManifestResourceStream("Fonia.Data.atc-names.csv").ShouldNotBeNull();
        return CsvTable.Read(stream);
    }

    private static List<Airport> Controlled() =>
        [.. Catalog.Airports.Where(a => a.HasFrequency(AirportFrequencyType.Ground) || a.HasFrequency(AirportFrequencyType.Tower))];

    [Fact]
    public void The_embedded_data_loads_quickly_and_without_warnings()
    {
        var watch = Stopwatch.StartNew();

        var catalog = AirportCatalog.Load();

        watch.Stop();
        catalog.Warnings.ShouldBeEmpty(string.Join(Environment.NewLine, catalog.Warnings));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "loading the airports must not slow the app down");
    }

    [Fact]
    public void The_catalog_has_the_airports_of_brazil_and_only_real_airports()
    {
        Catalog.Count.ShouldBeGreaterThan(5000);
        Catalog.Airports.Select(a => a.Type).Distinct().Order().ShouldBe([AirportType.Large, AirportType.Medium, AirportType.Small]);
    }

    [Fact]
    public void Every_airport_is_inside_brazil_and_its_islands()
    {
        // Catches swapped or corrupted coordinates after a refresh. Trindade is the easternmost, at about 29 degrees west.
        Catalog.Airports.ShouldAllBe(
            a => a.LatitudeDegrees >= -35 && a.LatitudeDegrees <= 6 && a.LongitudeDegrees >= -75 && a.LongitudeDegrees <= -28,
            "an airport has coordinates outside Brazil");
    }

    [Fact]
    public void Curitiba_is_there_with_runways_and_the_frequencies_the_solo_needs()
    {
        Catalog.TryGet("SBCT", out var curitiba).ShouldBeTrue();

        curitiba.Type.ShouldBe(AirportType.Large);
        curitiba.Runways.ShouldNotBeEmpty();
        curitiba.Runways.ShouldAllBe(r => r.Low.MagneticHeadingDegrees != null && r.High.MagneticHeadingDegrees != null);
        curitiba.HasFrequency(AirportFrequencyType.Ground).ShouldBeTrue();
        curitiba.HasFrequency(AirportFrequencyType.Tower).ShouldBeTrue();
        curitiba.SpokenName.ShouldBe("Curitiba");
    }

    [Fact]
    public void The_controlled_airports_were_not_lost_by_a_refresh()
    {
        Controlled().Count.ShouldBeGreaterThan(50);
        Catalog.Airports.Count(a => a.HasFrequency(AirportFrequencyType.Ground)).ShouldBeGreaterThan(25);
    }

    [Fact]
    public void Every_frequency_is_in_the_voice_band()
    {
        var outside = Catalog.Airports
            .SelectMany(a => a.Frequencies.Select(f => (a.Ident, f)))
            .Where(x => x.f.Frequency.MHz is < 108 or > 137)
            .Select(x => $"{x.Ident} {x.f.Type} {x.f.Frequency}")
            .ToList();

        outside.ShouldBeEmpty();
    }

    [Fact]
    public void Every_airport_with_a_ground_or_tower_frequency_has_an_entry_in_the_names_file()
    {
        var named = NamesFile().Rows.Select(r => r["icao"]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Controlled().Where(a => !named.Contains(a.Ident)).Select(a => a.Ident).ToList();

        missing.ShouldBeEmpty("airports with a controller and no spoken name (add them to data/atc-names.csv)");
    }

    [Fact]
    public void The_names_file_is_well_formed()
    {
        var rows = NamesFile().Rows;
        var allowedSources = new[] { "vatsim-br", "sugestao", "municipio" };

        rows.Select(r => r["icao"]).ShouldBeUnique("an airport appears twice in data/atc-names.csv");
        rows.ShouldAllBe(r => r["spoken_name"].Length > 0 && r["spoken_name"] == r["spoken_name"].Trim());
        rows.ShouldAllBe(r => allowedSources.Contains(r["source"]), "a source other than vatsim-br, sugestao or municipio");
        rows.ShouldAllBe(r => r["reviewed"] == "sim" || r["reviewed"] == "nao");
    }

    [Fact]
    public void Every_name_in_the_names_file_belongs_to_an_airport_in_the_data()
    {
        var stale = NamesFile().Rows.Select(r => r["icao"]).Where(icao => !Catalog.TryGet(icao, out _)).ToList();

        stale.ShouldBeEmpty("names of airports that are not in the data (a refresh may have dropped them)");
    }

    [Fact]
    public void A_reviewed_name_that_is_just_the_municipality_explains_why()
    {
        // "municipio" means "no better name known": it can be reviewed (the municipality is the right name), but
        // that is a decision, so it must be explicit: a note explains it.
        var unexplained = NamesFile().Rows
            .Where(r => r["source"] == "municipio" && r["reviewed"] == "sim" && r["notes"].Length == 0)
            .Select(r => r["icao"])
            .ToList();

        unexplained.ShouldBeEmpty();
    }

    [Fact]
    public void The_nearest_airport_to_the_apron_of_curitiba_is_curitiba()
    {
        var found = AirportCatalog.Load().Nearest(-25.5285, -49.1758);

        found.ShouldNotBeNull();
        found.Value.Airport.Ident.ShouldBe("SBCT");
        found.Value.Kilometers.ShouldBeLessThan(2);
    }

    [Fact]
    public void In_the_middle_of_the_ocean_the_nearest_airport_is_far()
    {
        var found = AirportCatalog.Load().Nearest(-30, -20);

        found.ShouldNotBeNull();
        found.Value.Kilometers.ShouldBeGreaterThan(500);
    }

    [Fact]
    public void A_helipad_or_a_closed_field_is_not_where_an_airplane_starts()
    {
        var catalog = AirportCatalog.Load();

        foreach (var airport in catalog.Airports.Where(a => a.Type is AirportType.Heliport or AirportType.Closed or AirportType.SeaplaneBase).Take(20))
        {
            catalog.Nearest(airport.LatitudeDegrees, airport.LongitudeDegrees)!.Value.Airport.Type
                .ShouldNotBeOneOf(AirportType.Heliport, AirportType.Closed, AirportType.SeaplaneBase);
        }
    }
}
