using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Fonia.Core.Airports;
using Fonia.Core.Sim;
using Fonia.Data.Aisweb;

namespace Fonia.Data;

/// <summary>
/// The airports the app knows, read from the OurAirports files (public domain) and from <c>data/atc-names.csv</c>.
/// Bad rows never stop the loading: they are left out and described in <see cref="Warnings"/>, because a typo in one of
/// 5,000 airports must not take the others down.
/// </summary>
public sealed class AirportCatalog : IAirportCatalog
{
    private readonly Dictionary<string, Airport> _byIdent;

    private AirportCatalog(Dictionary<string, Airport> byIdent, List<string> warnings)
    {
        _byIdent = byIdent;
        Airports = byIdent.Values;
        Warnings = warnings;
    }

    public IReadOnlyCollection<Airport> Airports { get; }

    public int Count => _byIdent.Count;

    /// <summary>What could not be used while loading, each with the line of the file it came from. Empty for clean data.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Loads the data embedded in this assembly: the airports of Brazil and the names file.</summary>
    public static AirportCatalog Load()
    {
        using var airports = Open("br-airports.csv");
        using var runways = Open("br-runways.csv");
        using var frequencies = Open("br-airport-frequencies.csv");
        using var names = Open("atc-names.csv");
        using var aisweb = Open("br-aisweb-frequencies.csv");

        return Create(airports, runways, frequencies, names, aisweb);
    }

    /// <summary>
    /// The airport nearest to a position and how far it is, among the ones an airplane can start from (not heliports, seaplane bases or
    /// closed fields); null only when the catalog has none.
    /// </summary>
    public (Airport Airport, double Kilometers)? Nearest(double latitudeDegrees, double longitudeDegrees)
    {
        Airport? best = null;
        var bestKm = double.MaxValue;

        foreach (var airport in Airports.Where(a => a.Type is AirportType.Large or AirportType.Medium or AirportType.Small))
        {
            var km = airport.DistanceKmTo(latitudeDegrees, longitudeDegrees);

            if (km < bestKm)
            {
                best = airport;
                bestKm = km;
            }
        }

        return best is null ? null : (best, bestKm);
    }

    /// <summary>
    /// Loads from four CSV streams (airports, runways, frequencies, names), in the OurAirports layout, and, when given, the frequencies of the
    /// ROTAER (<c>data/aisweb</c>), which replace the OurAirports ones for each kind of unit they have.
    /// </summary>
    /// <exception cref="DataFormatException">A file is not valid CSV (for example, empty).</exception>
    public static AirportCatalog Create(Stream airports, Stream runways, Stream frequencies, Stream names, Stream? aisweb = null)
    {
        var warnings = new List<string>();
        var spokenNames = ReadNames(CsvTable.Read(names), warnings);
        var runwaysByAirport = ReadRunways(CsvTable.Read(runways));
        var frequenciesByAirport = ReadFrequencies(CsvTable.Read(frequencies), warnings);
        var rotaerFrequencies = aisweb is null ? [] : AiswebFrequencyTable.Read(CsvTable.Read(aisweb), warnings);
        var byIdent = new Dictionary<string, Airport>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in CsvTable.Read(airports).Rows)
        {
            var ident = row["ident"].Trim();

            if (ident.Length == 0)
            {
                warnings.Add($"line {row.LineNumber}: a row has no ident; skipped.");
                continue;
            }

            if (!TryDouble(row["latitude_deg"], out var latitude) || latitude is null)
            {
                warnings.Add($"line {row.LineNumber}: airport {ident} has an invalid latitude '{row["latitude_deg"]}'; skipped.");
                continue;
            }

            if (!TryDouble(row["longitude_deg"], out var longitude) || longitude is null)
            {
                warnings.Add($"line {row.LineNumber}: airport {ident} has an invalid longitude '{row["longitude_deg"]}'; skipped.");
                continue;
            }

            if (byIdent.ContainsKey(ident))
            {
                warnings.Add($"line {row.LineNumber}: airport {ident} appears twice; the first row is used.");
                continue;
            }

            TryDouble(row["elevation_ft"], out var elevation);
            var municipality = row["municipality"].Trim();
            var name = row["name"].Trim();

            var (spokenName, source, reviewed) = spokenNames.TryGetValue(ident, out var entry)
                ? entry
                : (municipality.Length > 0 ? municipality : name, SpokenNameSource.Municipality, false);

            byIdent[ident] = new Airport(
                Ident: ident,
                Name: name,
                Municipality: municipality,
                Type: ParseType(row["type"]),
                LatitudeDegrees: latitude.Value,
                LongitudeDegrees: longitude.Value,
                ElevationFeet: elevation,
                IcaoCode: NullIfEmpty(row["icao_code"]),
                IataCode: NullIfEmpty(row["iata_code"]),
                SpokenName: spokenName,
                SpokenNameSource: source,
                SpokenNameReviewed: reviewed,
                Runways: runwaysByAirport.GetValueOrDefault(ident)?.Select(r => r.Runway).ToList() ?? [],
                Frequencies: AiswebFrequencyTable.Apply(
                    frequenciesByAirport.GetValueOrDefault(ident)?.Select(f => f.Frequency).ToList() ?? [],
                    rotaerFrequencies.GetValueOrDefault(ident)));
        }

        ReportOrphans(runwaysByAirport, "runway", byIdent, warnings);
        ReportOrphans(frequenciesByAirport, "frequency", byIdent, warnings);

        foreach (var ident in rotaerFrequencies.Keys.Where(k => !byIdent.ContainsKey(k)))
        {
            warnings.Add($"frequencies of {ident} in the AISWEB file ignored: no such airport in the data.");
        }

        foreach (var ident in spokenNames.Keys.Where(k => !byIdent.ContainsKey(k)))
        {
            warnings.Add($"name of {ident} ignored: no such airport in the data.");
        }

        return new AirportCatalog(byIdent, warnings);
    }

    public bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport airport)
    {
        if (string.IsNullOrWhiteSpace(ident))
        {
            airport = null;
            return false;
        }

        return _byIdent.TryGetValue(ident.Trim(), out airport);
    }

    private static Stream Open(string file)
    {
        var resource = $"Fonia.Data.{file}";

        return typeof(AirportCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The data file '{file}' is not embedded in the assembly (resource '{resource}').");
    }

    private static Dictionary<string, (string Name, SpokenNameSource Source, bool Reviewed)> ReadNames(CsvTable table, List<string> warnings)
    {
        var names = new Dictionary<string, (string, SpokenNameSource, bool)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var icao = row["icao"].Trim();
            var spoken = row["spoken_name"].Trim();

            if (icao.Length == 0 || spoken.Length == 0)
            {
                warnings.Add($"line {row.LineNumber}: a row of the names file has no icao or no spoken_name; skipped.");
                continue;
            }

            if (names.ContainsKey(icao))
            {
                warnings.Add($"line {row.LineNumber}: {icao} appears twice in the names file; the first entry is used.");
                continue;
            }

            var source = row["source"].Trim().ToLowerInvariant() switch
            {
                "vatsim-br" => SpokenNameSource.Reference,
                "sugestao" => SpokenNameSource.Suggestion,
                "municipio" => SpokenNameSource.Municipality,
                var unknown => WarnUnknownSource(unknown),
            };

            names[icao] = (spoken, source, string.Equals(row["reviewed"].Trim(), "sim", StringComparison.OrdinalIgnoreCase));

            SpokenNameSource WarnUnknownSource(string unknown)
            {
                warnings.Add($"line {row.LineNumber}: unknown source '{unknown}' for {icao} in the names file; treated as a suggestion.");
                return SpokenNameSource.Suggestion;
            }
        }

        return names;
    }

    private static Dictionary<string, List<(int Line, Runway Runway)>> ReadRunways(CsvTable table)
    {
        var byAirport = new Dictionary<string, List<(int, Runway)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var ident = row["airport_ident"].Trim();

            if (ident.Length == 0)
            {
                continue;
            }

            var runway = new Runway(
                Low: ReadEnd(row, "le"),
                High: ReadEnd(row, "he"),
                LengthFeet: TryInt(row["length_ft"]),
                WidthFeet: TryInt(row["width_ft"]),
                Surface: row["surface"].Trim(),
                Lighted: row["lighted"].Trim() == "1",
                Closed: row["closed"].Trim() == "1");

            if (!byAirport.TryGetValue(ident, out var list))
            {
                byAirport[ident] = list = [];
            }

            list.Add((row.LineNumber, runway));
        }

        return byAirport;
    }

    private static RunwayEnd ReadEnd(CsvRow row, string prefix)
    {
        var designator = RunwayDesignator.Normalize(row[$"{prefix}_ident"]);

        TryDouble(row[$"{prefix}_heading_degT"], out var trueHeading);
        TryDouble(row[$"{prefix}_elevation_ft"], out var elevation);

        return new RunwayEnd(designator, RunwayDesignator.MagneticHeadingDegrees(designator), trueHeading, elevation);
    }

    private static Dictionary<string, List<(int Line, AirportFrequency Frequency)>> ReadFrequencies(CsvTable table, List<string> warnings)
    {
        var byAirport = new Dictionary<string, List<(int, AirportFrequency)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var ident = row["airport_ident"].Trim();

            if (ident.Length == 0)
            {
                continue;
            }

            var text = row["frequency_mhz"].Trim();

            if (!TryDouble(text, out var mhz) || mhz is not > 0)
            {
                warnings.Add($"line {row.LineNumber}: frequency '{text}' of {ident} is not a valid number of MHz; ignored.");
                continue;
            }

            if (!byAirport.TryGetValue(ident, out var list))
            {
                byAirport[ident] = list = [];
            }

            list.Add((row.LineNumber, new AirportFrequency(ParseFrequencyType(row["type"]), Frequency.FromMhz(mhz.Value), row["description"].Trim())));
        }

        return byAirport;
    }

    private static void ReportOrphans<T>(Dictionary<string, List<(int Line, T Item)>> byAirport, string what, Dictionary<string, Airport> airports, List<string> warnings)
    {
        foreach (var (ident, items) in byAirport.Where(pair => !airports.ContainsKey(pair.Key)))
        {
            warnings.Add($"line {items[0].Line}: {what} of {ident} ignored: no such airport in the data ({items.Count} row(s)).");
        }
    }

    private static AirportType ParseType(string type)
    {
        return type.Trim() switch
        {
            "large_airport" => AirportType.Large,
            "medium_airport" => AirportType.Medium,
            "small_airport" => AirportType.Small,
            "heliport" => AirportType.Heliport,
            "seaplane_base" => AirportType.SeaplaneBase,
            "closed" => AirportType.Closed,
            _ => AirportType.Other,
        };
    }

    private static AirportFrequencyType ParseFrequencyType(string type)
    {
        return type.Trim().ToUpperInvariant() switch
        {
            "GND" => AirportFrequencyType.Ground,
            "TWR" => AirportFrequencyType.Tower,
            "APP" => AirportFrequencyType.Approach,
            // A second approach of the airport is listed with a name ("APP ACADEMIA").
            var name when name.StartsWith("APP ", StringComparison.Ordinal) => AirportFrequencyType.Approach,
            "ATIS" => AirportFrequencyType.Atis,
            "AFIS" => AirportFrequencyType.Afis,
            "CLD" => AirportFrequencyType.Delivery,
            "RDO" => AirportFrequencyType.Radio,
            "OPS" => AirportFrequencyType.Operations,
            _ => AirportFrequencyType.Other,
        };
    }

    // Empty is "no value" (null, and true); only text that is not a number is a failure.
    private static bool TryDouble(string text, out double? value)
    {
        value = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static int? TryInt(string text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? NullIfEmpty(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
