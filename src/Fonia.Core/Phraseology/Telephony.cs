using System.Reflection;
using System.Text;

namespace Fonia.Core.Phraseology;

/// <summary>
/// The telephony of the companies: what the controller says for the three-letter designator of a callsign ("GLO1234" is "GOL ..."), and the
/// reverse, to read a company the pilot said. The table is <c>data/telephony.csv</c>, kept in the repository and embedded here so that the core
/// needs no file (see <c>docs/decisions/airline-callsigns.md</c> for where the names come from).
/// </summary>
internal static class Telephony
{
    private sealed record Entry(string Designator, string Spoken, IReadOnlyList<string> Words);

    private static readonly IReadOnlyList<Entry> Entries = Load();

    private static readonly Dictionary<string, Entry> ByDesignator = Entries.GroupBy(e => e.Designator, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    // The longest names first, so that "lan cargo" is read before "lan".
    private static readonly IReadOnlyList<Entry> ForListening = [.. Entries.Where(e => e.Designator != "FAB").OrderByDescending(e => e.Words.Count)];

    /// <summary>What the controller says for this designator, or false when the table has none.</summary>
    public static bool TryGet(string designator, out string spoken)
    {
        if (ByDesignator.TryGetValue(designator, out var entry))
        {
            spoken = entry.Spoken;
            return true;
        }

        spoken = string.Empty;
        return false;
    }

    /// <summary>
    /// Whether the words from <paramref name="index"/> on are the telephony of a company ("air canada", "gol"); if so, the designator and
    /// how many words it took.
    /// </summary>
    public static bool TryRead(IReadOnlyList<string> tokens, int index, out string designator, out int length)
    {
        foreach (var entry in ForListening)
        {
            if (index + entry.Words.Count <= tokens.Count && entry.Words.Select((w, i) => tokens[index + i] == w).All(same => same))
            {
                designator = entry.Designator;
                length = entry.Words.Count;
                return true;
            }
        }

        designator = string.Empty;
        length = 0;
        return false;
    }

    private static List<Entry> Load()
    {
        using var stream = typeof(Telephony).Assembly.GetManifestResourceStream("telephony.csv")
            ?? throw new InvalidOperationException("The telephony table (telephony.csv) is not embedded in Fonia.Core.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var entries = new List<Entry>();

        _ = reader.ReadLine();

        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');

            if (fields.Length < 2 || fields[0].Length != 3)
            {
                continue;
            }

            var designator = fields[0].Trim().ToUpperInvariant();

            entries.Add(new Entry(designator, fields[1].Trim(), SpokenTokens.Tokenize(fields[1])));

            // The name the register gives is also a way to call the company ("gol transporte"), as the simulator or the pilot may have it.
            if (fields.Length > 2 && fields[2].Trim() is { Length: > 0 } registered && !string.Equals(registered, fields[1].Trim(), StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new Entry(designator, fields[1].Trim(), SpokenTokens.Tokenize(registered)));
            }
        }

        return entries;
    }
}
