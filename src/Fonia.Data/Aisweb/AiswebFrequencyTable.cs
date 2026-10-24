using System.Globalization;
using System.Text.RegularExpressions;
using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Data.Aisweb;

/// <summary>
/// The frequencies of <c>data/aisweb/br-aisweb-frequencies.csv</c> (the ROTAER, DECEA) as frequencies of an airport. The ROTAER is the official
/// source, so for each kind of unit it gives, its frequencies replace those of the OurAirports data; the kinds it does not give stay as they are.
/// The note the ROTAER writes beside a frequency changes what the frequency is: 121.5 comes as "EMERG." and must never be named as the Torre.
/// </summary>
internal static partial class AiswebFrequencyTable
{
    private const double EmergencyMhz = 121.5;

    // Only the units whose frequencies the ROTAER lists here replace the ones of the airport data. "Pátio" and "FCA" are not units the pilot calls.
    private static readonly Dictionary<string, AirportFrequencyType> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Torre"] = AirportFrequencyType.Tower,
        ["Solo"] = AirportFrequencyType.Ground,
        ["Tráfego"] = AirportFrequencyType.Delivery,
        ["ATIS"] = AirportFrequencyType.Atis,
        ["AFIS"] = AirportFrequencyType.Afis,
        ["Rádio"] = AirportFrequencyType.Radio,
        ["Operações"] = AirportFrequencyType.Operations,
    };

    /// <summary>What the note beside a frequency says about it.</summary>
    internal enum NoteKind
    {
        /// <summary>No note that matters: an ordinary frequency.</summary>
        Plain,

        /// <summary>It is the main one ("PRI", "Principal", "FREQ primária").</summary>
        Main,

        /// <summary>It answers, but it is not the one to name ("SRY", "ALTN", "FREQ reserva").</summary>
        Secondary,

        /// <summary>It is not a frequency of the unit for the pilot: emergency, military aircraft only.</summary>
        NotForPilot,
    }

    /// <summary>The frequencies by ICAO code, already grouped and with the primary of each kind chosen. Bad rows are left out and described in <paramref name="warnings"/>.</summary>
    public static Dictionary<string, List<AirportFrequency>> Read(CsvTable table, List<string> warnings)
    {
        var byAirport = new Dictionary<string, List<(AirportFrequencyType Type, AirportFrequency Frequency, NoteKind Kind)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.Rows)
        {
            var icao = row["icao"].Trim();
            var service = row["service"].Trim();

            if (icao.Length == 0)
            {
                warnings.Add($"line {row.LineNumber}: a row of the AISWEB file has no icao; skipped.");
                continue;
            }

            if (!Units.TryGetValue(service, out var type))
            {
                continue;
            }

            var text = row["frequency_mhz"].Trim();

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz) || !double.IsFinite(mhz) || mhz <= 0)
            {
                warnings.Add($"line {row.LineNumber}: frequency '{text}' of {icao} in the AISWEB file is not a valid number of MHz; ignored.");
                continue;
            }

            var kind = Classify(row["note"]);

            // 121.5 is the international emergency frequency; the ROTAER lists it on a Torre, sometimes without the note.
            if (kind == NoteKind.NotForPilot || Math.Abs(mhz - EmergencyMhz) < 0.0005)
            {
                continue;
            }

            if (!byAirport.TryGetValue(icao, out var list))
            {
                byAirport[icao] = list = [];
            }

            list.Add((type, new AirportFrequency(type, Frequency.FromMhz(mhz), service, Primary: false), kind));
        }

        return byAirport.ToDictionary(pair => pair.Key, pair => ChoosePrimaries(pair.Value), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The frequencies of an airport with those of the ROTAER in place of the airport data's for each kind of unit the ROTAER has; the rest is
    /// kept. Both lists come in the order they have.
    /// </summary>
    public static IReadOnlyList<AirportFrequency> Apply(IReadOnlyList<AirportFrequency> ofAirport, IReadOnlyList<AirportFrequency>? fromRotaer)
    {
        if (fromRotaer is not { Count: > 0 })
        {
            return ofAirport;
        }

        var replaced = fromRotaer.Select(f => f.Type).ToHashSet();

        return [.. ofAirport.Where(f => !replaced.Contains(f.Type)), .. fromRotaer];
    }

    /// <summary>Reads the free text of the ROTAER note. Tolerant on purpose: the notes are written by hand, in capitals or not, with or without accents.</summary>
    internal static NoteKind Classify(string note)
    {
        if (NotForPilotNote().IsMatch(note))
        {
            return NoteKind.NotForPilot;
        }

        if (SecondaryNote().IsMatch(note))
        {
            return NoteKind.Secondary;
        }

        return MainNote().IsMatch(note) ? NoteKind.Main : NoteKind.Plain;
    }

    // One primary per kind of unit: the one marked as main, else the first that is not secondary, else none (all are secondary).
    private static List<AirportFrequency> ChoosePrimaries(List<(AirportFrequencyType Type, AirportFrequency Frequency, NoteKind Kind)> all)
    {
        var result = new List<AirportFrequency>();

        foreach (var group in all.GroupBy(f => f.Type))
        {
            var distinct = group.DistinctBy(f => f.Frequency.Frequency).ToList();
            var primary = distinct.FirstOrDefault(f => f.Kind == NoteKind.Main);

            if (primary == default)
            {
                primary = distinct.FirstOrDefault(f => f.Kind == NoteKind.Plain);
            }

            result.AddRange(distinct
                .OrderBy(f => primary != default && f.Frequency.Frequency == primary.Frequency.Frequency ? 0 : 1)
                .Select(f => f.Frequency with { Primary = primary != default && f.Frequency.Frequency == primary.Frequency.Frequency }));
        }

        return result;
    }

    [GeneratedRegex(@"\bEMERG|ACFT\s+MIL", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotForPilotNote();

    [GeneratedRegex(@"\b(SRY|ALTN)\b|ALTERNATIV|SECUND[ÁA]RI|RESERVA", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecondaryNote();

    [GeneratedRegex(@"\bPRI\b|PRINCIPAL|PRIM[ÁA]RI", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MainNote();
}
