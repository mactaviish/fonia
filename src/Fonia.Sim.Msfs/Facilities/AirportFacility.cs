namespace Fonia.Sim.Msfs;

// What the simulator says about an airport, as it says it: no interpretation, the numbers of its enums kept (the SDK of MSFS lists them under
// SimConnect_AddToFacilityDefinition). The spike (--dump-layout) writes this and the conversion to the app's AirportLayout comes after the
// real data has been seen. Positions of taxi points and parking spots are offsets in meters from the airport reference point.

/// <summary>The airport itself. The counts are what the simulator says it has, to be compared with what it sent.</summary>
public sealed record RawAirport(
    string Icao,
    string Region,
    string Name,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMeters,
    float MagneticVariationDegrees,
    int RunwayCount,
    int FrequencyCount,
    int TaxiPointCount,
    int TaxiParkingCount,
    int TaxiPathCount,
    int TaxiNameCount);

/// <summary>A runway: its center, heading and size. Number is 1 to 36 (37 and up are compass points); designator is 0 none, 1 L, 2 R, 3 C, 4 W, 5 A, 6 B.</summary>
public sealed record RawRunway(
    double LatitudeDegrees,
    double LongitudeDegrees,
    double AltitudeMeters,
    float HeadingDegrees,
    float LengthMeters,
    float WidthMeters,
    int Surface,
    int PrimaryNumber,
    int PrimaryDesignator,
    int SecondaryNumber,
    int SecondaryDesignator);

/// <summary>A radio frequency. Type: 1 ATIS, 2 MULTICOM, 3 UNICOM, 4 CTAF, 5 GROUND, 6 TOWER, 7 CLEARANCE, 8 APPROACH, 9 DEPARTURE, 10 CENTER, 11 FSS, 12 AWOS, 13 ASOS, 14 CPT, 15 GCO.</summary>
public sealed record RawFrequency(int Type, int Hertz, string Name)
{
    /// <summary>The frequency in whole kHz, as the app keeps it.</summary>
    public int KiloHertz => Hertz / 1000;

    /// <summary>The name of <see cref="Type"/> in the SDK, or its number when the SDK has none.</summary>
    public string TypeName => Type switch
    {
        0 => "NONE",
        1 => "ATIS",
        2 => "MULTICOM",
        3 => "UNICOM",
        4 => "CTAF",
        5 => "GROUND",
        6 => "TOWER",
        7 => "CLEARANCE",
        8 => "APPROACH",
        9 => "DEPARTURE",
        10 => "CENTER",
        11 => "FSS",
        12 => "AWOS",
        13 => "ASOS",
        14 => "CPT",
        15 => "GCO",
        _ => Type.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}

/// <summary>A point of the taxi graph. Index is its place in the list: the paths refer to it. Type: 1 normal, 2 hold short, 4 ILS hold short, 5 and 6 the same without being drawn.</summary>
public sealed record RawTaxiPoint(int Index, int Type, int Orientation, float BiasX, float BiasZ);

/// <summary>A stretch between two points (or a point and a parking spot) by index. Type: 1 taxi, 2 runway, 3 parking, 4 path, 5 closed, 6 vehicle, 7 road, 8 painted line.</summary>
public sealed record RawTaxiPath(int Type, float WidthMeters, int RunwayNumber, int RunwayDesignator, int Start, int End, uint NameIndex);

/// <summary>A taxiway name ("A", "B2"); the paths refer to it by index.</summary>
public sealed record RawTaxiName(int Index, string Name);

/// <summary>A parking spot. Type: 1 to 7 ramps, 8 to 10 gates, 11 dock, 12 fuel, 13 vehicle, 14 ramp, 15 gate. Name and Suffix are the SDK's enum (1 parking, 10 gate, 12 to 37 gate A to Z...).</summary>
public sealed record RawParking(
    int Index,
    int Type,
    int TaxiPointType,
    int Name,
    int Suffix,
    uint Number,
    int Orientation,
    float HeadingDegrees,
    float RadiusMeters,
    float BiasX,
    float BiasZ);

/// <summary>How one request to the simulator went: what was asked, what came, and what went wrong.</summary>
public sealed record FacilityRequestOutcome(string Name, int Items, bool Ended, TimeSpan Elapsed, IReadOnlyList<string> Problems);

/// <summary>Everything the simulator said about one airport, with how each request went.</summary>
/// <param name="Airport">Null when the simulator did not send the airport itself (it does not know the code, or the request failed).</param>
public sealed record AirportFacility(
    RawAirport? Airport,
    IReadOnlyList<RawRunway> Runways,
    IReadOnlyList<RawFrequency> Frequencies,
    IReadOnlyList<RawTaxiPoint> TaxiPoints,
    IReadOnlyList<RawTaxiPath> TaxiPaths,
    IReadOnlyList<RawTaxiName> TaxiNames,
    IReadOnlyList<RawParking> Parkings,
    IReadOnlyList<FacilityRequestOutcome> Requests,
    TimeSpan Elapsed);

/// <summary>The simulator could not be asked: it is not running, the connection failed, or SimConnect is not available on this machine.</summary>
public sealed class FacilityReadException : Exception
{
    public FacilityReadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
