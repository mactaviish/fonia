using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Sim;
using Fonia.Data.Aisweb;

namespace Fonia.Data.Tests;

public class AiswebFrequencyTableTests
{
    private const string Airports = """
        id,ident,type,name,latitude_deg,longitude_deg,elevation_ft,iso_region,municipality,scheduled_service,icao_code,iata_code,gps_code,local_code
        1,"SBXX","medium_airport","Field Xx",-23.5,-46.6,2000,"BR-SP","Cidade Xx","yes","SBXX",,"SBXX",
        2,"SBYY","small_airport","Field Yy",-22.5,-45.6,1000,"BR-SP","Cidade Yy","no",,,,
        """;

    private const string Runways = "airport_ident,length_ft,width_ft,surface,lighted,closed,le_ident,le_heading_degT,le_elevation_ft,he_ident,he_heading_degT,he_elevation_ft\n";

    // The airport data of SBXX has a Torre and an approach; SBYY only a Torre.
    private const string Frequencies = """
        airport_ident,type,description,frequency_mhz
        "SBXX","TWR","XX TWR",118.55
        "SBXX","APP","XX APP",119.95
        "SBXX","GND","XX GND",121.9
        "SBYY","TWR","YY TWR",118.1
        """;

    private const string Names = "icao,spoken_name,source,reviewed,airport_name,notes\n";

    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    private static AirportCatalog Catalog(string aisweb) =>
        AirportCatalog.Create(Utf8(Airports), Utf8(Runways), Utf8(Frequencies), Utf8(Names), Utf8("icao,name,city,uf,service,callsign,frequency_mhz,note\n" + aisweb));

    private static Airport Get(AirportCatalog catalog, string ident)
    {
        catalog.TryGet(ident, out var airport).ShouldBeTrue();
        return airport;
    }

    [Theory]
    [InlineData("", "Plain")]
    [InlineData("OPR NAV Brasil", "Plain")]
    [InlineData("(DLY 1000-2200)", "Plain")]
    [InlineData("D-ATIS", "Plain")]
    [InlineData("EMERG", "NotForPilot")]
    [InlineData("EMERG.", "NotForPilot")]
    [InlineData("Emergência", "NotForPilot")]
    [InlineData("EMERG; DCL", "NotForPilot")]
    [InlineData("Somente ACFT MIL.", "NotForPilot")]
    [InlineData("SRY", "Secondary")]
    [InlineData("ALTN", "Secondary")]
    [InlineData("Alternativa", "Secondary")]
    [InlineData("FREQ reserva", "Secondary")]
    [InlineData("FREQ. secundária a critério da TWR", "Secondary")]
    [InlineData("PRI", "Main")]
    [InlineData("Principal", "Main")]
    [InlineData("FREQ primária", "Main")]
    [InlineData("prestado pelo APP", "Plain")]
    public void The_note_of_the_rotaer_says_what_the_frequency_is(string note, string expected)
    {
        AiswebFrequencyTable.Classify(note).ToString().ShouldBe(expected);
    }

    [Fact]
    public void The_emergency_frequency_is_never_a_frequency_of_the_unit()
    {
        var catalog = Catalog("""
            SBXX,Field,Cidade,SP,Torre,Xx,118.400,
            SBXX,Field,Cidade,SP,Torre,Xx,121.500,EMERG.
            """);

        var tower = Get(catalog, "SBXX").Frequencies.Where(f => f.Type == AirportFrequencyType.Tower).ToList();

        tower.Select(f => f.Frequency).ShouldBe([Frequency.FromMhz(118.4)]);
    }

    [Fact]
    public void The_emergency_frequency_is_left_out_even_when_the_rotaer_gives_it_no_note()
    {
        var catalog = Catalog("""
            SBXX,Field,Cidade,SP,Torre,Xx,118.400,
            SBXX,Field,Cidade,SP,Torre,Xx,121.500,
            """);

        Get(catalog, "SBXX").Frequencies.Where(f => f.Type == AirportFrequencyType.Tower).Select(f => f.Frequency)
            .ShouldBe([Frequency.FromMhz(118.4)]);
    }

    [Fact]
    public void For_the_units_the_rotaer_has_its_frequencies_replace_the_airport_data_and_the_rest_stays()
    {
        var catalog = Catalog("SBXX,Field,Cidade,SP,Torre,Xx,118.150,");

        var airport = Get(catalog, "SBXX");

        airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(Frequency.FromMhz(118.15));
        airport.Frequencies.Count(f => f.Type == AirportFrequencyType.Tower).ShouldBe(1);
        airport.FrequencyOf(AirportFrequencyType.Approach)!.Frequency.ShouldBe(Frequency.FromMhz(119.95));
        airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency.ShouldBe(Frequency.FromMhz(121.9));
        Get(catalog, "SBYY").FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(Frequency.FromMhz(118.1));
    }

    [Fact]
    public void Only_one_frequency_of_a_unit_is_the_one_to_name_and_it_is_not_a_secondary()
    {
        var catalog = Catalog("""
            SBXX,Field,Cidade,SP,Torre,Xx,118.150,SRY
            SBXX,Field,Cidade,SP,Torre,Xx,127.150,
            SBXX,Field,Cidade,SP,Torre,Xx,132.750,
            """);

        var tower = Get(catalog, "SBXX").Frequencies.Where(f => f.Type == AirportFrequencyType.Tower).ToList();

        tower.Count.ShouldBe(3);
        tower.Where(f => f.Primary).Select(f => f.Frequency).ShouldBe([Frequency.FromMhz(127.15)]);
        Get(catalog, "SBXX").FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(Frequency.FromMhz(127.15));
    }

    [Fact]
    public void A_frequency_marked_as_the_main_one_wins_over_the_order()
    {
        var catalog = Catalog("""
            SBXX,Field,Cidade,SP,Torre,Xx,127.150,
            SBXX,Field,Cidade,SP,Torre,Xx,118.150,Principal
            """);

        Get(catalog, "SBXX").FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(Frequency.FromMhz(118.15));
    }

    [Fact]
    public void When_every_frequency_of_a_unit_is_secondary_none_is_named_but_all_answer()
    {
        var catalog = Catalog("""
            SBXX,Field,Cidade,SP,Torre,Xx,118.150,SRY
            SBXX,Field,Cidade,SP,Torre,Xx,127.150,ALTN
            """);

        var airport = Get(catalog, "SBXX");

        airport.HasFrequency(AirportFrequencyType.Tower).ShouldBeFalse();
        airport.Frequencies.Count(f => f.Type == AirportFrequencyType.Tower).ShouldBe(2);
    }

    [Fact]
    public void The_service_names_of_the_rotaer_become_the_kinds_of_unit()
    {
        var catalog = Catalog("""
            SBYY,Field,Cidade,SP,Solo,Yy,121.700,
            SBYY,Field,Cidade,SP,Tráfego,Yy,121.000,
            SBYY,Field,Cidade,SP,ATIS,Yy,127.750,
            SBYY,Field,Cidade,SP,AFIS,Yy,123.450,
            SBYY,Field,Cidade,SP,Pátio,Yy,130.675,
            """);

        var airport = Get(catalog, "SBYY");

        airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency.ShouldBe(Frequency.FromMhz(121.7));
        airport.FrequencyOf(AirportFrequencyType.Delivery)!.Frequency.ShouldBe(Frequency.FromMhz(121.0));
        airport.FrequencyOf(AirportFrequencyType.Atis)!.Frequency.ShouldBe(Frequency.FromMhz(127.75));
        airport.FrequencyOf(AirportFrequencyType.Afis)!.Frequency.ShouldBe(Frequency.FromMhz(123.45));
        airport.Frequencies.Any(f => f.Frequency == Frequency.FromMhz(130.675)).ShouldBeFalse("a Pátio is not a unit the pilot calls");
    }

    [Fact]
    public void Bad_rows_and_airports_that_are_not_in_the_data_are_left_out_with_a_warning()
    {
        var catalog = Catalog("""
            SBXX,Field,Cidade,SP,Torre,Xx,abc,
            ,Field,Cidade,SP,Torre,Xx,118.100,
            ZZZZ,Field,Cidade,SP,Torre,Zz,118.100,
            """);

        catalog.Warnings.Count.ShouldBe(3);
        Get(catalog, "SBXX").FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(Frequency.FromMhz(118.55));
    }

    [Fact]
    public void Without_the_file_the_airport_data_is_used_as_before()
    {
        var catalog = AirportCatalog.Create(Utf8(Airports), Utf8(Runways), Utf8(Frequencies), Utf8(Names));

        Get(catalog, "SBXX").FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(Frequency.FromMhz(118.55));
    }

    [Fact]
    public void The_embedded_file_has_no_emergency_frequency_as_a_tower_and_gives_guarulhos_its_tower()
    {
        var catalog = AirportCatalog.Load();

        catalog.Warnings.ShouldBeEmpty(string.Join(Environment.NewLine, catalog.Warnings));
        catalog.Airports
            .SelectMany(a => a.Frequencies.Where(f => f.Type is AirportFrequencyType.Tower or AirportFrequencyType.Ground))
            .ShouldNotContain(f => f.Frequency == Frequency.FromMhz(121.5));

        catalog.TryGet("SBGR", out var guarulhos).ShouldBeTrue();
        guarulhos.FrequencyOf(AirportFrequencyType.Tower).ShouldNotBeNull();
    }
}
