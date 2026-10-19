using System.Buffers.Binary;
using System.Text;

namespace Fonia.Sim.Msfs;

internal enum FieldKind
{
    Float64,
    Float32,
    Int32,
    UInt32,
    Text8,
    Text32,
    Text64,
}

internal readonly record struct FieldSpec(string Name, FieldKind Kind)
{
    public int Size => Kind switch
    {
        FieldKind.Float64 => 8,
        FieldKind.Text8 => 8,
        FieldKind.Text32 => 32,
        FieldKind.Text64 => 64,
        _ => 4,
    };
}

/// <summary>
/// One thing asked of the simulator: an airport child (or the airport itself) and its fields, in the order the simulator packs them in the
/// data of a message. The names are those of SimConnect_AddToFacilityDefinition in the SDK of MSFS. Every schema is asked in its own request,
/// so that a field the simulator does not know costs only that one and not the rest of the airport.
/// </summary>
internal sealed record FacilitySchema(string Name, FacilityKind Kind, string? Child, IReadOnlyList<FieldSpec> Fields)
{
    /// <summary>The bytes of one item.</summary>
    public int Size => Fields.Sum(f => f.Size);

    /// <summary>The names to hand to AddToFacilityDefinition: the fields inside the OPEN and CLOSE of the airport and, if any, of the child.</summary>
    public IReadOnlyList<string> Definition()
    {
        var names = new List<string> { "OPEN AIRPORT" };

        if (Child is not null)
        {
            names.Add($"OPEN {Child}");
        }

        names.AddRange(Fields.Select(f => f.Name));

        if (Child is not null)
        {
            names.Add($"CLOSE {Child}");
        }

        names.Add("CLOSE AIRPORT");

        return names;
    }

    public static readonly FacilitySchema Airport = new(
        "AIRPORT",
        FacilityKind.Airport,
        null,
        [
            new("LATITUDE", FieldKind.Float64),
            new("LONGITUDE", FieldKind.Float64),
            new("ALTITUDE", FieldKind.Float64),
            new("MAGVAR", FieldKind.Float32),
            new("NAME", FieldKind.Text32),
            new("ICAO", FieldKind.Text8),
            new("REGION", FieldKind.Text8),
            new("N_RUNWAYS", FieldKind.Int32),
            new("N_FREQUENCIES", FieldKind.Int32),
            new("N_TAXI_POINTS", FieldKind.Int32),
            new("N_TAXI_PARKINGS", FieldKind.Int32),
            new("N_TAXI_PATHS", FieldKind.Int32),
            new("N_TAXI_NAMES", FieldKind.Int32),
        ]);

    public static readonly FacilitySchema Runway = new(
        "RUNWAY",
        FacilityKind.Runway,
        "RUNWAY",
        [
            new("LATITUDE", FieldKind.Float64),
            new("LONGITUDE", FieldKind.Float64),
            new("ALTITUDE", FieldKind.Float64),
            new("HEADING", FieldKind.Float32),
            new("LENGTH", FieldKind.Float32),
            new("WIDTH", FieldKind.Float32),
            new("SURFACE", FieldKind.Int32),
            new("PRIMARY_NUMBER", FieldKind.Int32),
            new("PRIMARY_DESIGNATOR", FieldKind.Int32),
            new("SECONDARY_NUMBER", FieldKind.Int32),
            new("SECONDARY_DESIGNATOR", FieldKind.Int32),
        ]);

    public static readonly FacilitySchema Frequency = new(
        "FREQUENCY",
        FacilityKind.Frequency,
        "FREQUENCY",
        [
            new("TYPE", FieldKind.Int32),
            new("FREQUENCY", FieldKind.Int32),
            new("NAME", FieldKind.Text64),
        ]);

    public static readonly FacilitySchema TaxiPoint = new(
        "TAXI_POINT",
        FacilityKind.TaxiPoint,
        "TAXI_POINT",
        [
            new("TYPE", FieldKind.Int32),
            new("ORIENTATION", FieldKind.Int32),
            new("BIAS_X", FieldKind.Float32),
            new("BIAS_Z", FieldKind.Float32),
        ]);

    public static readonly FacilitySchema TaxiPath = new(
        "TAXI_PATH",
        FacilityKind.TaxiPath,
        "TAXI_PATH",
        [
            new("TYPE", FieldKind.Int32),
            new("WIDTH", FieldKind.Float32),
            new("RUNWAY_NUMBER", FieldKind.Int32),
            new("RUNWAY_DESIGNATOR", FieldKind.Int32),
            new("START", FieldKind.Int32),
            new("END", FieldKind.Int32),
            new("NAME_INDEX", FieldKind.UInt32),
        ]);

    public static readonly FacilitySchema TaxiName = new(
        "TAXI_NAME",
        FacilityKind.TaxiName,
        "TAXI_NAME",
        [new("NAME", FieldKind.Text32)]);

    public static readonly FacilitySchema TaxiParking = new(
        "TAXI_PARKING",
        FacilityKind.TaxiParking,
        "TAXI_PARKING",
        [
            new("TYPE", FieldKind.Int32),
            new("TAXI_POINT_TYPE", FieldKind.Int32),
            new("NAME", FieldKind.Int32),
            new("SUFFIX", FieldKind.Int32),
            new("NUMBER", FieldKind.UInt32),
            new("ORIENTATION", FieldKind.Int32),
            new("HEADING", FieldKind.Float32),
            new("RADIUS", FieldKind.Float32),
            new("BIAS_X", FieldKind.Float32),
            new("BIAS_Z", FieldKind.Float32),
        ]);

    /// <summary>In the order they are asked: the airport first, so the counts are known before the lists.</summary>
    public static readonly IReadOnlyList<FacilitySchema> All = [Airport, Runway, Frequency, TaxiPoint, TaxiPath, TaxiName, TaxiParking];
}

/// <summary>Reads the fields of one item in order, as the simulator packs them (little endian, no padding, text null-terminated in a fixed size).</summary>
internal ref struct FacilityFieldReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _at;

    public readonly int Position => _at;

    public double Double() => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));

    public float Single() => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(Take(4)));

    public int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public string Text(int size)
    {
        var bytes = Take(size);
        var end = bytes.IndexOf((byte)0);

        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]).Trim();
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (_at + count > _data.Length)
        {
            throw new FormatException($"The data has {_data.Length} bytes; a field of {count} at {_at} does not fit.");
        }

        var slice = _data.Slice(_at, count);
        _at += count;

        return slice;
    }
}

/// <summary>Turns the data of a facility message into the raw records. The order of the reads is that of the fields of the schema.</summary>
internal static class FacilityParser
{
    public static RawAirport Airport(ReadOnlySpan<byte> data)
    {
        var r = new FacilityFieldReader(data);

        var latitude = r.Double();
        var longitude = r.Double();
        var altitude = r.Double();
        var variation = r.Single();
        var name = r.Text(32);
        var icao = r.Text(8);
        var region = r.Text(8);

        return new RawAirport(icao, region, name, latitude, longitude, altitude, variation, r.Int(), r.Int(), r.Int(), r.Int(), r.Int(), r.Int());
    }

    public static RawRunway Runway(ReadOnlySpan<byte> data)
    {
        var r = new FacilityFieldReader(data);

        return new RawRunway(r.Double(), r.Double(), r.Double(), r.Single(), r.Single(), r.Single(), r.Int(), r.Int(), r.Int(), r.Int(), r.Int());
    }

    public static RawFrequency Frequency(ReadOnlySpan<byte> data)
    {
        var r = new FacilityFieldReader(data);

        return new RawFrequency(r.Int(), r.Int(), r.Text(64));
    }

    public static RawTaxiPoint TaxiPoint(int index, ReadOnlySpan<byte> data)
    {
        var r = new FacilityFieldReader(data);

        return new RawTaxiPoint(index, r.Int(), r.Int(), r.Single(), r.Single());
    }

    public static RawTaxiPath TaxiPath(ReadOnlySpan<byte> data)
    {
        var r = new FacilityFieldReader(data);

        return new RawTaxiPath(r.Int(), r.Single(), r.Int(), r.Int(), r.Int(), r.Int(), r.UInt());
    }

    public static RawTaxiName TaxiName(int index, ReadOnlySpan<byte> data)
    {
        var r = new FacilityFieldReader(data);

        return new RawTaxiName(index, r.Text(32));
    }

    public static RawParking Parking(int index, ReadOnlySpan<byte> data)
    {
        var r = new FacilityFieldReader(data);

        return new RawParking(index, r.Int(), r.Int(), r.Int(), r.Int(), r.UInt(), r.Int(), r.Single(), r.Single(), r.Single(), r.Single());
    }
}
