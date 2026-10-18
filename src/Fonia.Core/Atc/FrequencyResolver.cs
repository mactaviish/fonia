using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// Who answers is decided by the frequency the pilot tuned, as on a real radio: the Solo on its frequency, the Torre on
/// its own, nobody on one that belongs to no unit of the airport. It only looks at the units that talk (Solo, Torre, AFIS and
/// the Controle): the ATIS has a frequency too, but nobody there answers a call.
/// </summary>
public static class FrequencyResolver
{
    /// <summary>
    /// The airport context for what the pilot says on <paramref name="com1"/>. Without a reading (null) the airport's own
    /// unit stays, which is how the text mode without a simulator works.
    /// </summary>
    public static AirportContext Apply(AirportContext airport, Frequency? com1)
    {
        ArgumentNullException.ThrowIfNull(airport);

        return com1 is { } tuned ? airport with { Unit = UnitOn(airport.Airport, tuned), Tuned = tuned } : airport;
    }

    /// <summary>Whether <paramref name="frequency"/> is the ATIS of the airport: nobody answers there, the broadcast plays.</summary>
    public static bool IsAtis(Airport airport, Frequency frequency)
    {
        ArgumentNullException.ThrowIfNull(airport);

        return airport.Frequencies.Any(f => f.Type == AirportFrequencyType.Atis && f.Frequency == frequency);
    }

    /// <summary>The unit of the airport on <paramref name="frequency"/>, or <see cref="ServiceUnit.None"/>.</summary>
    public static ServiceUnit UnitOn(Airport airport, Frequency frequency)
    {
        ArgumentNullException.ThrowIfNull(airport);

        foreach (var entry in airport.Frequencies.Where(f => f.Frequency == frequency))
        {
            switch (entry.Type)
            {
                case AirportFrequencyType.Ground:
                    return ServiceUnit.Ground;
                case AirportFrequencyType.Tower:
                    return ServiceUnit.Tower;
                case AirportFrequencyType.Afis:
                    return ServiceUnit.Afis;
                case AirportFrequencyType.Approach:
                    return ServiceUnit.Approach;
            }
        }

        return ServiceUnit.None;
    }
}
