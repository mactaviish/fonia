using Fonia.Data.Aisweb;

namespace Fonia.Data.Tests;

public class AiswebRotaerTests
{
    // Invented aerodrome, in the shape of the ROTAER answer (extra elements the app does not use are left in on purpose).
    private const string Answer = """
        <?xml version="1.0" encoding="UTF-8"?><aisweb>
          <status>Active</status>
          <dt>2026-01-02</dt>
          <AeroCode>sbxx</AeroCode>
          <name><![CDATA[Aeroporto   de Exemplo]]></name>
          <city><![CDATA[Cidade Exemplo]]></city>
          <uf>ZZ</uf>
          <services>
            <service compl="" type="COM">
              <type>Torre</type>
              <callsign>Exemplo</callsign>
              <freqs>
                <freq compl="">118.400</freq>
                <freq compl="2">121.500</freq>
                <freq compl="1, 3">132.750</freq>
                <freq compl="">sem numero</freq>
              </freqs>
            </service>
            <service compl="" type="COM">
              <type>Solo</type>
              <callsign>Exemplo</callsign>
              <freqs><freq compl="">121.700</freq></freqs>
            </service>
            <service compl="" type="COM">
              <type>Vazio</type>
              <callsign>Exemplo</callsign>
              <freqs></freqs>
            </service>
            <service type="NAV">
              <type>ILS/DME</type>
              <freq>111.1</freq>
            </service>
          </services>
          <compls count="3">
            <compl cod="2.18" n="1"><![CDATA[DCL.]]></compl>
            <compl cod="2.18" n="2"><![CDATA[
                EMERG.
            ]]></compl>
            <compl cod="2.18" n="3"><![CDATA[D-ATIS]]></compl>
          </compls>
        </aisweb>
        """;

    [Fact]
    public void Reads_the_identity_of_the_aerodrome_and_cleans_the_spaces()
    {
        var aerodrome = AiswebRotaer.Parse(Answer);

        aerodrome.ShouldNotBeNull();
        aerodrome.Icao.ShouldBe("SBXX");
        aerodrome.Name.ShouldBe("Aeroporto de Exemplo");
        aerodrome.City.ShouldBe("Cidade Exemplo");
        aerodrome.Uf.ShouldBe("ZZ");
        aerodrome.UpdatedOn.ShouldBe("2026-01-02");
    }

    [Fact]
    public void Keeps_only_the_communication_services_that_have_a_frequency()
    {
        var aerodrome = AiswebRotaer.Parse(Answer)!;

        aerodrome.Services.Select(s => s.Type).ShouldBe(["Torre", "Solo"]);
        aerodrome.Services[0].Callsign.ShouldBe("Exemplo");
    }

    [Fact]
    public void Gives_each_frequency_the_notes_it_points_to_and_skips_what_is_not_a_number()
    {
        var tower = AiswebRotaer.Parse(Answer)!.Services[0];

        tower.Frequencies.ShouldBe(
        [
            new AiswebFrequency(118.4, string.Empty),
            new AiswebFrequency(121.5, "EMERG."),
            new AiswebFrequency(132.75, "DCL.; D-ATIS"),
        ]);
    }

    [Theory]
    [InlineData("<aisweb></aisweb>")]
    [InlineData("<aisweb><AeroCode> </AeroCode></aisweb>")]
    public void An_answer_without_an_aerodrome_is_null(string xml)
    {
        AiswebRotaer.Parse(xml).ShouldBeNull();
    }

    [Fact]
    public void Text_that_is_not_xml_is_a_format_error()
    {
        Should.Throw<DataFormatException>(() => AiswebRotaer.Parse("not xml at all"));
    }
}
