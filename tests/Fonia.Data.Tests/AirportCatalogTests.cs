using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Sim;
using Fonia.Data;

namespace Fonia.Data.Tests;

public class AirportCatalogTests
{
    // Rows copied from the real OurAirports files (SBCT), plus a few made up to exercise bad data.
    private const string Airports = """
        id,ident,type,name,latitude_deg,longitude_deg,elevation_ft,iso_region,municipality,scheduled_service,icao_code,iata_code,gps_code,local_code
        5891,"SBCT","large_airport","Curitiba-Afonso Pena International Airport",-25.5285,-49.1758,2988,"BR-PR","Curitiba","yes","SBCT","CWB","SBCT","PR0001"
        5912,"SBGL","large_airport","Rio de Janeiro Galeão – Tom Jobim International Airport",-22.809999,-43.250556,28,"BR-RJ","Rio De Janeiro","yes","SBGL","GIG","SBGL",
        9001,"SBXX","small_airport","Small Field",-23.5,-46.6,,"BR-SP","São Paulo","no",,,,
        9002,"BR-0001","small_airport","No Municipality Field",-10.5,-50.5,100,"BR-TO",,"no",,,,
        9003,"SBHP","heliport","Hospital Heliport",-23.6,-46.7,2500,"BR-SP","São Paulo","no",,,,
        9004,"SBCL","closed","Closed Field",-22.0,-47.0,500,"BR-SP","Campinas","no",,,,
        9005,"SBBL","small_airport","Bad Latitude",abc,-47.0,500,"BR-SP","Campinas","no",,,,
        9006,,"small_airport","No Ident",-22.0,-47.0,500,"BR-SP","Campinas","no",,,,
        """;

    private const string Runways = """
        airport_ident,length_ft,width_ft,surface,lighted,closed,le_ident,le_heading_degT,le_elevation_ft,he_ident,he_heading_degT,he_elevation_ft
        "SBCT",5899,148,"ASP",0,0,"11",,,"29",,
        "SBCT",7277,148,"ASP",1,0,"15",136,2987,"33",314,2987
        "SBGL",13123,148,"ASP",1,1,"10","","","28","",""
        "SBHP",,,"CON",0,0,"H1",,,"",,
        "NOPE",5000,100,"ASP",0,0,"09",,,"27",,
        "SBXX",3000,60,"GRS",0,0,"9L",,,"27R",,
        """;

    private const string Frequencies = """
        airport_ident,type,description,frequency_mhz
        "SBCT","APP","CURITIBA APP",119.95
        "SBCT","ATIS","ATIS",127.8
        "SBCT","GND","GND",121.9
        "SBCT","TWR","CURITIBA TWR",118.55
        "SBCT","ZZZ","Something else",123.45
        "SBCT","GND","Bad frequency",0
        "SBCT","TWR","Not a number",abc
        "NOPE","TWR","TWR",118.1
        """;

    private const string Names = """
        icao,spoken_name,source,reviewed,airport_name,notes
        SBCT,Curitiba,sugestao,nao,Curitiba-Afonso Pena International Airport,
        SBGL,Galeão,vatsim-br,sim,Rio de Janeiro Galeão,
        ZZZZ,Nada,sugestao,nao,Not in the airport data,
        SBXX,Pequeno,chute,nao,Small Field,
        SBCT,Outro,sugestao,nao,Duplicate,
        """;

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    private static AirportCatalog Catalog(
        string airports = Airports,
        string runways = Runways,
        string frequencies = Frequencies,
        string names = Names)
    {
        return AirportCatalog.Create(Utf8(airports), Utf8(runways), Utf8(frequencies), Utf8(names));
    }

    private static Airport Get(AirportCatalog catalog, string ident)
    {
        catalog.TryGet(ident, out var airport).ShouldBeTrue($"{ident} should be in the catalog");
        return airport;
    }

    [Fact]
    public void An_airport_is_read_with_its_data()
    {
        var curitiba = Get(Catalog(), "SBCT");

        curitiba.Ident.ShouldBe("SBCT");
        curitiba.Name.ShouldBe("Curitiba-Afonso Pena International Airport");
        curitiba.Municipality.ShouldBe("Curitiba");
        curitiba.Type.ShouldBe(AirportType.Large);
        curitiba.LatitudeDegrees.ShouldBe(-25.5285);
        curitiba.LongitudeDegrees.ShouldBe(-49.1758);
        curitiba.ElevationFeet.ShouldBe(2988);
        curitiba.IcaoCode.ShouldBe("SBCT");
        curitiba.IataCode.ShouldBe("CWB");
    }

    [Fact]
    public void Missing_optional_data_is_null()
    {
        var small = Get(Catalog(), "SBXX");

        small.ElevationFeet.ShouldBeNull();
        small.IcaoCode.ShouldBeNull();
        small.IataCode.ShouldBeNull();
    }

    [Theory]
    [InlineData("large_airport", AirportType.Large)]
    [InlineData("medium_airport", AirportType.Medium)]
    [InlineData("small_airport", AirportType.Small)]
    [InlineData("heliport", AirportType.Heliport)]
    [InlineData("seaplane_base", AirportType.SeaplaneBase)]
    [InlineData("closed", AirportType.Closed)]
    [InlineData("balloonport", AirportType.Other)]
    [InlineData("", AirportType.Other)]
    public void The_airport_type_is_mapped(string type, AirportType expected)
    {
        var airports = $"ident,type,name,latitude_deg,longitude_deg\nSBTT,{type},Test,-23.0,-46.0\n";

        Get(Catalog(airports: airports, runways: "airport_ident\n", frequencies: "airport_ident\n", names: "icao\n"), "SBTT").Type.ShouldBe(expected);
    }

    [Fact]
    public void Runways_come_with_both_ends_and_a_heading_derived_from_the_number()
    {
        var runways = Get(Catalog(), "SBCT").Runways;

        runways.Count.ShouldBe(2);

        var first = runways[0];
        first.Name.ShouldBe("11/29");
        first.Low.MagneticHeadingDegrees.ShouldBe(110);
        first.High.MagneticHeadingDegrees.ShouldBe(290);
        first.Low.TrueHeadingDegrees.ShouldBeNull(); // the data has no heading for this runway
        first.LengthFeet.ShouldBe(5899);
        first.WidthFeet.ShouldBe(148);
        first.Surface.ShouldBe("ASP");
        first.Lighted.ShouldBeFalse();
        first.Closed.ShouldBeFalse();
    }

    [Fact]
    public void A_heading_the_data_does_have_is_kept_as_the_true_heading()
    {
        var second = Get(Catalog(), "SBCT").Runways[1];

        second.Name.ShouldBe("15/33");
        second.Low.MagneticHeadingDegrees.ShouldBe(150);
        second.Low.TrueHeadingDegrees.ShouldBe(136);
        second.High.MagneticHeadingDegrees.ShouldBe(330);
        second.High.TrueHeadingDegrees.ShouldBe(314);
        second.Low.ElevationFeet.ShouldBe(2987);
        second.Lighted.ShouldBeTrue();
    }

    [Fact]
    public void A_closed_runway_is_flagged()
    {
        Get(Catalog(), "SBGL").Runways.Single().Closed.ShouldBeTrue();
    }

    [Fact]
    public void Designators_are_normalized_and_a_helipad_has_no_heading()
    {
        var parallel = Get(Catalog(), "SBXX").Runways.Single();
        parallel.Low.Designator.ShouldBe("09L");
        parallel.High.Designator.ShouldBe("27R");

        var helipad = Get(Catalog(), "SBHP").Runways.Single();
        helipad.Low.Designator.ShouldBe("H1");
        helipad.Low.MagneticHeadingDegrees.ShouldBeNull();
        helipad.LengthFeet.ShouldBeNull();
    }

    [Fact]
    public void Frequencies_are_read_by_type_in_whole_khz()
    {
        var curitiba = Get(Catalog(), "SBCT");

        curitiba.FrequencyOf(AirportFrequencyType.Ground)!.Frequency.ShouldBe(new Frequency(121900));
        curitiba.FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(new Frequency(118550));
        curitiba.FrequencyOf(AirportFrequencyType.Approach)!.Frequency.ShouldBe(new Frequency(119950));
        curitiba.FrequencyOf(AirportFrequencyType.Atis)!.Frequency.ShouldBe(new Frequency(127800));
        curitiba.FrequencyOf(AirportFrequencyType.Tower)!.Description.ShouldBe("CURITIBA TWR");
    }

    [Fact]
    public void An_unknown_frequency_type_is_other_and_a_bad_frequency_is_dropped()
    {
        var frequencies = Get(Catalog(), "SBCT").Frequencies;

        frequencies.Count.ShouldBe(5); // 4 usual + 1 unknown type; the 0 MHz and the "abc" ones are gone
        frequencies.Single(f => f.Type == AirportFrequencyType.Other).Frequency.ShouldBe(new Frequency(123450));
        frequencies.Count(f => f.Type == AirportFrequencyType.Ground).ShouldBe(1);
    }

    [Theory]
    [InlineData("GND", AirportFrequencyType.Ground)]
    [InlineData("TWR", AirportFrequencyType.Tower)]
    [InlineData("APP", AirportFrequencyType.Approach)]
    [InlineData("ATIS", AirportFrequencyType.Atis)]
    [InlineData("AFIS", AirportFrequencyType.Afis)]
    [InlineData("CLD", AirportFrequencyType.Delivery)]
    [InlineData("RDO", AirportFrequencyType.Radio)]
    [InlineData("OPS", AirportFrequencyType.Operations)]
    [InlineData("UNICOM", AirportFrequencyType.Other)]
    [InlineData("", AirportFrequencyType.Other)]
    public void The_frequency_type_is_mapped(string type, AirportFrequencyType expected)
    {
        var frequencies = $"airport_ident,type,description,frequency_mhz\nSBCT,{type},x,121.9\n";

        Get(Catalog(frequencies: frequencies), "SBCT").Frequencies.Single().Type.ShouldBe(expected);
    }

    [Fact]
    public void The_spoken_name_comes_from_the_names_file()
    {
        var galeao = Get(Catalog(), "SBGL");

        galeao.Municipality.ShouldBe("Rio De Janeiro"); // what the data says is not what the controller says
        galeao.SpokenName.ShouldBe("Galeão");
        galeao.SpokenNameSource.ShouldBe(SpokenNameSource.Reference);
        galeao.SpokenNameReviewed.ShouldBeTrue();

        var curitiba = Get(Catalog(), "SBCT");
        curitiba.SpokenName.ShouldBe("Curitiba");
        curitiba.SpokenNameSource.ShouldBe(SpokenNameSource.Suggestion);
        curitiba.SpokenNameReviewed.ShouldBeFalse();
    }

    [Fact]
    public void Without_an_entry_the_spoken_name_falls_back_to_the_municipality_and_then_to_the_airport_name()
    {
        var sao = Get(Catalog(), "SBHP");
        sao.SpokenName.ShouldBe("São Paulo");
        sao.SpokenNameSource.ShouldBe(SpokenNameSource.Municipality);
        sao.SpokenNameReviewed.ShouldBeFalse();

        var noMunicipality = Get(Catalog(), "BR-0001");
        noMunicipality.SpokenName.ShouldBe("No Municipality Field");
        noMunicipality.SpokenNameSource.ShouldBe(SpokenNameSource.Municipality);
    }

    [Fact]
    public void Lookups_ignore_case_and_surrounding_spaces()
    {
        var catalog = Catalog();

        catalog.TryGet("sbct", out var a).ShouldBeTrue();
        catalog.TryGet("  SBCT ", out var b).ShouldBeTrue();
        a.ShouldBeSameAs(b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SBZZ")]
    public void An_airport_that_is_not_there_is_not_found(string? ident)
    {
        Catalog().TryGet(ident, out _).ShouldBeFalse();
    }

    [Fact]
    public void Rows_that_cannot_be_used_are_left_out_and_reported_with_their_line()
    {
        var catalog = Catalog();

        catalog.Count.ShouldBe(6); // SBCT, SBGL, SBXX, BR-0001, SBHP, SBCL; the bad latitude and the no-ident rows are out
        catalog.TryGet("SBBL", out _).ShouldBeFalse();

        catalog.Warnings.ShouldContain(w => w.Contains("line 8", StringComparison.Ordinal) && w.Contains("latitude", StringComparison.OrdinalIgnoreCase));
        catalog.Warnings.ShouldContain(w => w.Contains("line 9", StringComparison.Ordinal) && w.Contains("ident", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Data_that_points_at_a_missing_airport_is_reported_not_fatal()
    {
        var warnings = Catalog().Warnings;

        warnings.ShouldContain(w => w.Contains("NOPE", StringComparison.Ordinal) && w.Contains("runway", StringComparison.OrdinalIgnoreCase));
        warnings.ShouldContain(w => w.Contains("NOPE", StringComparison.Ordinal) && w.Contains("frequency", StringComparison.OrdinalIgnoreCase));
        warnings.ShouldContain(w => w.Contains("ZZZZ", StringComparison.Ordinal) && w.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_bad_frequency_is_reported()
    {
        var warnings = Catalog().Warnings;

        warnings.ShouldContain(w => w.Contains("SBCT", StringComparison.Ordinal) && w.Contains("frequency", StringComparison.OrdinalIgnoreCase) && w.Contains("abc", StringComparison.Ordinal));
        warnings.ShouldContain(w => w.Contains("SBCT", StringComparison.Ordinal) && w.Contains("frequency", StringComparison.OrdinalIgnoreCase) && w.Contains('0'));
    }

    [Fact]
    public void An_unknown_source_and_a_duplicate_in_the_names_file_are_reported()
    {
        var catalog = Catalog();

        catalog.Warnings.ShouldContain(w => w.Contains("chute", StringComparison.Ordinal));
        catalog.Warnings.ShouldContain(w => w.Contains("SBCT", StringComparison.Ordinal) && w.Contains("twice", StringComparison.OrdinalIgnoreCase));

        // The unknown source is treated as a suggestion, and for a duplicate the first entry wins.
        Get(catalog, "SBXX").SpokenNameSource.ShouldBe(SpokenNameSource.Suggestion);
        Get(catalog, "SBCT").SpokenName.ShouldBe("Curitiba");
    }

    [Fact]
    public void Clean_data_has_no_warnings()
    {
        var airports = """
            ident,type,name,latitude_deg,longitude_deg,municipality
            SBCT,large_airport,Curitiba,-25.5,-49.1,Curitiba
            """;
        var names = "icao,spoken_name,source,reviewed\nSBCT,Curitiba,sugestao,nao\n";

        Catalog(airports, "airport_ident\nSBCT\n", "airport_ident,type,description,frequency_mhz\nSBCT,GND,GND,121.9\n", names)
            .Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void A_file_with_no_header_is_an_error()
    {
        Should.Throw<DataFormatException>(() => Catalog(airports: string.Empty));
    }

    [Fact]
    public void The_catalog_lists_every_airport_it_loaded()
    {
        Catalog().Airports.Select(a => a.Ident).ShouldBe(["SBCT", "SBGL", "SBXX", "BR-0001", "SBHP", "SBCL"], ignoreOrder: true);
    }
}
