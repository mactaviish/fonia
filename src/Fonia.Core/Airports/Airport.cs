using System.Diagnostics.CodeAnalysis;
using Fonia.Core.Sim;

namespace Fonia.Core.Airports;

public enum AirportType
{
    Large,
    Medium,
    Small,
    Heliport,
    SeaplaneBase,
    Closed,
    Other,
}

/// <summary>What a radio frequency of an airport is for.</summary>
public enum AirportFrequencyType
{
    /// <summary>Solo (Ground).</summary>
    Ground,

    /// <summary>Torre (Tower).</summary>
    Tower,

    /// <summary>Controle de aproximação (Approach).</summary>
    Approach,

    Atis,

    /// <summary>Serviço de Informação de Voo de Aeródromo (Rádio): information only, no clearances.</summary>
    Afis,

    /// <summary>Tráfego (Clearance Delivery).</summary>
    Delivery,

    Radio,
    Operations,
    Other,
}

/// <summary>Where a spoken name came from. See <c>data/atc-names.csv</c>.</summary>
public enum SpokenNameSource
{
    /// <summary>It appears in a reference (the VATSIM Brasil examples).</summary>
    Reference,

    /// <summary>Someone's suggestion, to be confirmed.</summary>
    Suggestion,

    /// <summary>No entry in the names file: the municipality from the airport data.</summary>
    Municipality,
}

public sealed record AirportFrequency(AirportFrequencyType Type, Frequency Frequency, string Description);

/// <summary>One end of a runway (the runway "15/33" has the ends "15" and "33").</summary>
/// <param name="MagneticHeadingDegrees">Approximate, from the number of the designator; null if it is not a runway number.</param>
/// <param name="TrueHeadingDegrees">As given by the airport data, when it has one.</param>
public sealed record RunwayEnd(string Designator, double? MagneticHeadingDegrees, double? TrueHeadingDegrees, double? ElevationFeet);

public sealed record Runway(
    RunwayEnd Low,
    RunwayEnd High,
    int? LengthFeet,
    int? WidthFeet,
    string Surface,
    bool Lighted,
    bool Closed)
{
    public IReadOnlyList<RunwayEnd> Ends => [Low, High];

    /// <summary>"15/33".</summary>
    public string Name => $"{Low.Designator}/{High.Designator}";
}

/// <summary>An airport as the ATC needs it: where it is, its runways, its frequencies and how its units are called.</summary>
/// <param name="Ident">The code in the airport data: the ICAO code, or a local code for aerodromes without one.</param>
/// <param name="SpokenName">What the controller says after the unit: "Solo <b>Galeão</b>".</param>
public sealed record Airport(
    string Ident,
    string Name,
    string Municipality,
    AirportType Type,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double? ElevationFeet,
    string? IcaoCode,
    string? IataCode,
    string SpokenName,
    SpokenNameSource SpokenNameSource,
    bool SpokenNameReviewed,
    IReadOnlyList<Runway> Runways,
    IReadOnlyList<AirportFrequency> Frequencies)
{
    /// <summary>The distance in kilometers along the great circle from the airport to a position (haversine, a sphere of 6371 km).</summary>
    public double DistanceKmTo(double latitudeDegrees, double longitudeDegrees) =>
        Sim.Geo.DistanceKm(LatitudeDegrees, LongitudeDegrees, latitudeDegrees, longitudeDegrees);

    /// <summary>The first frequency listed for <paramref name="type"/>, or null if the airport has none.</summary>
    public AirportFrequency? FrequencyOf(AirportFrequencyType type)
    {
        return Frequencies.FirstOrDefault(f => f.Type == type);
    }

    public bool HasFrequency(AirportFrequencyType type) => FrequencyOf(type) is not null;
}

public interface IAirportCatalog
{
    /// <summary>Finds an airport by its <see cref="Airport.Ident"/> (case does not matter).</summary>
    bool TryGet(string? ident, [MaybeNullWhen(false)] out Airport airport);
}
