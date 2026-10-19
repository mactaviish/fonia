namespace Fonia.Core.Airports;

/// <summary>
/// Puts the frequencies the simulator knows into an airport of the airport data. What the checks against the real frequencies showed: the
/// simulator was right where the airport data was wrong or had nothing (a Torre, a Solo, a Tráfego, an ATIS), so for the kinds of unit it gives,
/// its frequencies replace those of the airport data. What it does not give is left as it is: an incomplete answer must not leave an airport
/// without the unit that the airport data had right.
/// </summary>
public static class AirportFrequencyMerge
{
    // The units whose frequencies the simulator can replace. Its advisory frequencies (UNICOM, MULTICOM) and the rest are not taken.
    private static readonly AirportFrequencyType[] Replaced =
    [
        AirportFrequencyType.Ground,
        AirportFrequencyType.Tower,
        AirportFrequencyType.Approach,
        AirportFrequencyType.Atis,
        AirportFrequencyType.Delivery,
    ];

    /// <summary>
    /// The airport with the frequencies of the simulator in place of its own for each kind of unit the simulator has. Of several frequencies of a
    /// unit, the primary (the one named when passing a flight on) is the one the airport data already had, if the simulator has it too; otherwise
    /// the only one; otherwise, for the Controle (the many sectors of an approach), none, so that no sector is named by chance; for the others,
    /// the first the simulator lists.
    /// </summary>
    public static Airport Apply(Airport airport, IReadOnlyList<AirportFrequency> fromSimulator)
    {
        ArgumentNullException.ThrowIfNull(airport);
        ArgumentNullException.ThrowIfNull(fromSimulator);

        var merged = new List<AirportFrequency>();
        var replaced = new HashSet<AirportFrequencyType>();

        foreach (var type in Replaced)
        {
            var theirs = fromSimulator
                .Where(f => f.Type == type)
                .DistinctBy(f => f.Frequency)
                .ToList();

            if (theirs.Count == 0)
            {
                continue;
            }

            replaced.Add(type);

            var confirmed = airport.Frequencies
                .Where(f => f.Type == type && f.Primary)
                .Select(f => (AirportFrequency?)theirs.FirstOrDefault(t => t.Frequency == f.Frequency))
                .FirstOrDefault(t => t is not null);

            var primary = confirmed
                ?? (theirs.Count == 1 || type != AirportFrequencyType.Approach ? theirs[0] : null);

            merged.AddRange(theirs
                .OrderBy(t => primary is not null && t.Frequency == primary.Frequency ? 0 : 1)
                .Select(t => t with { Primary = primary is not null && t.Frequency == primary.Frequency }));
        }

        if (replaced.Count == 0)
        {
            return airport;
        }

        return airport with { Frequencies = [.. airport.Frequencies.Where(f => !replaced.Contains(f.Type)), .. merged] };
    }
}
