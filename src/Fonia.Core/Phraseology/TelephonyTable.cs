using System.Text;

namespace Fonia.Core.Phraseology;

/// <summary>
/// The telephony of the companies: what the controller says for the three-letter designator of a callsign ("GLO1234" is "GOL ..."), and the
/// reverse, to read a company the pilot said. <see cref="BuiltIn"/> is <c>data/telephony.csv</c>, kept in the repository and embedded here so
/// that the core needs no file (see <c>docs/decisions/airline-callsigns.md</c> for where the names come from). A user with a virtual airline
/// adds its own with <see cref="With"/> (said in capitals, like the others); the table is immutable, so each part of the pipeline gets the one it should use.
/// </summary>
public sealed class TelephonyTable
{
    private sealed record Entry(string Designator, string Spoken, IReadOnlyList<string> Words);

    private static readonly Lazy<TelephonyTable> Embedded = new(() => new TelephonyTable(Load()));

    private readonly IReadOnlyList<Entry> _entries;
    private readonly Dictionary<string, Entry> _byDesignator;

    // The longest names first, so that "lan cargo" is read before "lan".
    private readonly IReadOnlyList<Entry> _forListening;

    private TelephonyTable(IReadOnlyList<Entry> entries)
    {
        _entries = entries;
        _byDesignator = entries.GroupBy(e => e.Designator, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _forListening = [.. entries.Where(e => e.Designator != "FAB").OrderByDescending(e => e.Words.Count)];
    }

    /// <summary>The telephonies of the table in the repository.</summary>
    public static TelephonyTable BuiltIn => Embedded.Value;

    /// <summary>
    /// A table with one more company: <paramref name="designator"/> ("WVR") is said and heard as <paramref name="spoken"/> ("Wave Air"). It wins
    /// over a company of the same designator already in the table.
    /// </summary>
    /// <exception cref="ArgumentException">The designator is not three letters, or the telephony has no words.</exception>
    public TelephonyTable With(string designator, string spoken)
    {
        ArgumentNullException.ThrowIfNull(designator);
        ArgumentNullException.ThrowIfNull(spoken);

        var code = designator.Trim().ToUpperInvariant();

        if (code.Length != 3 || !code.All(char.IsAsciiLetter))
        {
            throw new ArgumentException($"The designator '{designator}' is not three letters (like WVR).", nameof(designator));
        }

        var words = SpokenTokens.Tokenize(spoken);

        if (words.Count == 0)
        {
            throw new ArgumentException($"The telephony of {code} has no words.", nameof(spoken));
        }

        // The company this designator was before is gone, so it is neither said nor read any more.
        return new TelephonyTable([new Entry(code, spoken.Trim().ToUpperInvariant(), words), .. _entries.Where(e => e.Designator != code)]);
    }

    /// <summary>What the controller says for this designator, or false when the table has none.</summary>
    public bool TryGet(string designator, out string spoken)
    {
        if (_byDesignator.TryGetValue(designator, out var entry))
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
    public bool TryRead(IReadOnlyList<string> tokens, int index, out string designator, out int length)
    {
        foreach (var entry in _forListening)
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
        using var stream = typeof(TelephonyTable).Assembly.GetManifestResourceStream("telephony.csv")
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
