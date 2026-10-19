using Fonia.Core.Airports;

namespace Fonia.Data;

/// <summary>
/// Which runway lands and which takes off at the airports that keep them apart, from <c>data/runway-use.csv</c> (informed by the maintainer, who
/// listens to the Torres; see <c>data/README.md</c>). An airport that is not in the file uses one runway for both, as always.
/// </summary>
public sealed class RunwayUseTable
{
    private readonly Dictionary<string, List<RunwayUseRule>> _byAirport;

    private RunwayUseTable(Dictionary<string, List<RunwayUseRule>> byAirport, IReadOnlyList<string> warnings)
    {
        _byAirport = byAirport;
        Warnings = warnings;
    }

    /// <summary>What could not be used while loading, each with the line of the file it came from. Empty for clean data.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Loads the file embedded in this assembly.</summary>
    public static RunwayUseTable Load()
    {
        const string resource = "Fonia.Data.runway-use.csv";

        using var stream = typeof(RunwayUseTable).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The data file 'runway-use.csv' is not embedded in the assembly (resource '{resource}').");

        return Create(stream);
    }

    /// <exception cref="DataFormatException">The file is not valid CSV.</exception>
    public static RunwayUseTable Create(Stream csv)
    {
        var table = CsvTable.Read(csv);
        var byAirport = new Dictionary<string, List<RunwayUseRule>>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var row in table.Rows)
        {
            var icao = row["icao"].Trim();
            var arrival = row["arrival"].Trim();
            var departure = row["departure"].Trim();

            if (icao.Length == 0 || !RunwayDesignator.TryParse(arrival, out _, out _) || !RunwayDesignator.TryParse(departure, out _, out _))
            {
                warnings.Add($"line {row.LineNumber}: a row of the runway use file has no icao or a runway that is not a runway; skipped.");
                continue;
            }

            if (RunwayDesignator.AreSame(arrival, departure))
            {
                warnings.Add($"line {row.LineNumber}: {icao} lands and takes off from the same runway ({arrival}); there is nothing to keep apart; skipped.");
                continue;
            }

            if (!byAirport.TryGetValue(icao, out var rules))
            {
                byAirport[icao] = rules = [];
            }

            rules.Add(new RunwayUseRule(RunwayDesignator.Normalize(arrival), RunwayDesignator.Normalize(departure)));
        }

        return new RunwayUseTable(byAirport, warnings);
    }

    /// <summary>The rules of the airport (case does not matter); empty when it has none.</summary>
    public IReadOnlyList<RunwayUseRule> RulesFor(string? icao) =>
        icao is not null && _byAirport.TryGetValue(icao.Trim(), out var rules) ? rules : [];
}
