using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Fonia.Data.Aisweb;

/// <summary>One frequency of a communication service; <see cref="Note"/> is the complement the ROTAER attaches to it ("EMERG.", "DCL.").</summary>
public sealed record AiswebFrequency(double Mhz, string Note);

/// <summary>A communication service of an aerodrome ("Torre", "Solo", "Controle"), the name it is called by and its frequencies.</summary>
public sealed record AiswebService(string Type, string Callsign, IReadOnlyList<AiswebFrequency> Frequencies);

/// <summary>What the ROTAER says about one aerodrome, reduced to what the app uses.</summary>
public sealed record AiswebAerodrome(string Icao, string Name, string City, string Uf, string UpdatedOn, IReadOnlyList<AiswebService> Services);

/// <summary>Reads the answer of the AISWEB API for <c>area=rotaer&amp;icaoCode=...</c> (XML, one aerodrome).</summary>
public static partial class AiswebRotaer
{
    /// <summary>Parses the XML answer. Null when it is not an aerodrome (no ICAO code), for example an empty or error answer.</summary>
    /// <exception cref="DataFormatException">The text is not valid XML.</exception>
    public static AiswebAerodrome? Parse(string xml)
    {
        XDocument document;

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new DataFormatException($"The AISWEB answer is not valid XML: {ex.Message}", ex.LineNumber);
        }

        var root = document.Root;
        var icao = Text(root?.Element("AeroCode")).ToUpperInvariant();

        if (root is null || icao.Length == 0)
        {
            return null;
        }

        var complements = root.Element("compls")?.Elements("compl")
            .Select(c => (Number: (string?)c.Attribute("n") ?? string.Empty, Text: Collapse(c.Value)))
            .Where(c => c.Number.Length > 0 && c.Text.Length > 0)
            .GroupBy(c => c.Number)
            .ToDictionary(g => g.Key, g => g.First().Text)
            ?? [];

        var services = new List<AiswebService>();

        foreach (var service in root.Descendants("service").Where(s => (string?)s.Attribute("type") == "COM"))
        {
            var frequencies = new List<AiswebFrequency>();

            foreach (var frequency in service.Element("freqs")?.Elements("freq") ?? [])
            {
                if (!double.TryParse(frequency.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz) || mhz is not > 0)
                {
                    continue;
                }

                var notes = ((string?)frequency.Attribute("compl") ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(n => complements.GetValueOrDefault(n))
                    .OfType<string>();

                frequencies.Add(new AiswebFrequency(mhz, string.Join("; ", notes)));
            }

            var type = Text(service.Element("type"));

            if (type.Length > 0 && frequencies.Count > 0)
            {
                services.Add(new AiswebService(type, Text(service.Element("callsign")), frequencies));
            }
        }

        return new AiswebAerodrome(icao, Text(root.Element("name")), Text(root.Element("city")), Text(root.Element("uf")), Text(root.Element("dt")), services);
    }

    private static string Text(XElement? element) => element is null ? string.Empty : Collapse(element.Value);

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
