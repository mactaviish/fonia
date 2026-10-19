using System.Buffers.Binary;
using System.Text;

namespace Fonia.Sim.Msfs.Tests;

/// <summary>Builds the data of a facility message by hand, field by field in the order of the schema: little endian, no padding.</summary>
internal sealed class FacilityBytes
{
    private readonly List<byte> _bytes = [];

    public FacilityBytes Double(double value)
    {
        var buffer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, BitConverter.DoubleToInt64Bits(value));
        _bytes.AddRange(buffer);

        return this;
    }

    public FacilityBytes Single(float value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(value));
        _bytes.AddRange(buffer);

        return this;
    }

    public FacilityBytes Int(int value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        _bytes.AddRange(buffer);

        return this;
    }

    public FacilityBytes UInt(uint value) => Int((int)value);

    public FacilityBytes Text(string value, int size)
    {
        var buffer = new byte[size];
        Encoding.UTF8.GetBytes(value, buffer);
        _bytes.AddRange(buffer);

        return this;
    }

    public byte[] ToArray() => [.. _bytes];
}

/// <summary>The items of a small airport, as the simulator would send them.</summary>
internal static class FacilityTestData
{
    public static byte[] Airport() =>
        new FacilityBytes().Double(-25.5).Double(-49.1).Double(900).Single(-20.5f).Text("Curitiba Test", 32).Text("SBXX", 8).Text("SB", 8)
            .Int(1).Int(2).Int(3).Int(1).Int(1).Int(1).ToArray();

    public static byte[] Runway() =>
        new FacilityBytes().Double(-25.5).Double(-49.1).Double(900).Single(110.5f).Single(1800).Single(45).Int(4).Int(11).Int(0).Int(29).Int(0).ToArray();

    public static byte[] Frequency(int type, int hertz, string name) => new FacilityBytes().Int(type).Int(hertz).Text(name, 64).ToArray();

    public static byte[] TaxiPoint(int type, float x, float z) => new FacilityBytes().Int(type).Int(0).Single(x).Single(z).ToArray();

    public static byte[] TaxiPath(int type, int runwayNumber, int start, int end, uint nameIndex) =>
        new FacilityBytes().Int(type).Single(23).Int(runwayNumber).Int(0).Int(start).Int(end).UInt(nameIndex).ToArray();

    public static byte[] TaxiName(string name) => new FacilityBytes().Text(name, 32).ToArray();

    public static byte[] Parking() =>
        new FacilityBytes().Int(10).Int(1).Int(10).Int(0).UInt(3).Int(0).Single(180).Single(20).Single(-50).Single(75).ToArray();
}

/// <summary>
/// Answers the requests of the reader with what it is told to: for each definition it remembers which part of the airport it asks for (by its
/// OPEN line) and, on the request, sends that part's items and then the end. A part can be refused, left without an end, or answered with an
/// exception.
/// </summary>
internal sealed class FakeFacilityTransport : IFacilityTransport
{
    private readonly Dictionary<uint, string> _definitions = [];

    /// <summary>The items to send for a part ("RUNWAY", "TAXI_POINT"...); the airport itself is "AIRPORT".</summary>
    public Dictionary<string, List<(uint Index, byte[] Data)>> Items { get; } = [];

    public HashSet<string> RefuseDefinition { get; } = [];

    public HashSet<string> NeverEnd { get; } = [];

    public HashSet<string> RaiseException { get; } = [];

    public HashSet<string> Ambiguous { get; } = [];

    public List<string> Requested { get; } = [];

    public Exception? OpenFails { get; set; }

    public bool? IsMsfs2024 { get; set; }

    public bool Disposed { get; private set; }

    public event Action<FacilityMessage>? MessageReceived;

    public Task OpenAsync(CancellationToken ct) => OpenFails is null ? Task.CompletedTask : Task.FromException(OpenFails);

    public int Define(uint defineId, IReadOnlyList<string> fields)
    {
        var part = fields.Count > 1 && fields[1].StartsWith("OPEN ", StringComparison.Ordinal) ? fields[1][5..] : "AIRPORT";

        _definitions[defineId] = part;

        return RefuseDefinition.Contains(part) ? unchecked((int)0x80004005) : 0;
    }

    public int Request(uint defineId, uint requestId, string icao)
    {
        var part = _definitions[defineId];
        var kind = part switch
        {
            "AIRPORT" => FacilityKind.Airport,
            "RUNWAY" => FacilityKind.Runway,
            "FREQUENCY" => FacilityKind.Frequency,
            "TAXI_POINT" => FacilityKind.TaxiPoint,
            "TAXI_PATH" => FacilityKind.TaxiPath,
            "TAXI_NAME" => FacilityKind.TaxiName,
            "TAXI_PARKING" => FacilityKind.TaxiParking,
            _ => FacilityKind.Other,
        };

        Requested.Add($"{part}:{icao}");

        if (Ambiguous.Contains(part))
        {
            MessageReceived?.Invoke(new FacilityAmbiguousMessage());
            return 0;
        }

        if (RaiseException.Contains(part))
        {
            MessageReceived?.Invoke(new FacilityExceptionMessage(21, 7, 3));
        }

        var items = Items.GetValueOrDefault(part) ?? [];

        foreach (var (index, data) in items)
        {
            MessageReceived?.Invoke(new FacilityDataMessage(requestId, kind, part != "AIRPORT", index, (uint)items.Count, data));
        }

        if (!NeverEnd.Contains(part))
        {
            MessageReceived?.Invoke(new FacilityEndMessage(requestId));
        }

        return 0;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;

        return ValueTask.CompletedTask;
    }

    /// <summary>A small airport: one runway, three frequencies, three taxi points, one runway path and one taxiway path, a name, one parking spot.</summary>
    public static FakeFacilityTransport SmallAirport()
    {
        var transport = new FakeFacilityTransport();

        transport.Items["AIRPORT"] = [(0, FacilityTestData.Airport())];
        transport.Items["RUNWAY"] = [(0, FacilityTestData.Runway())];
        transport.Items["FREQUENCY"] =
        [
            (0, FacilityTestData.Frequency(6, 118_275_000, "TORRE")),
            (1, FacilityTestData.Frequency(5, 121_900_000, "SOLO")),
            (2, FacilityTestData.Frequency(1, 127_800_000, "ATIS")),
        ];
        transport.Items["TAXI_POINT"] =
        [
            (0, FacilityTestData.TaxiPoint(1, 100, 20)),
            (1, FacilityTestData.TaxiPoint(2, 120, 20)),
            (2, FacilityTestData.TaxiPoint(1, 140, 20)),
        ];
        transport.Items["TAXI_PATH"] =
        [
            (0, FacilityTestData.TaxiPath(1, 0, 0, 1, 0)),
            (1, FacilityTestData.TaxiPath(2, 11, 1, 2, 0)),
        ];
        transport.Items["TAXI_NAME"] = [(0, FacilityTestData.TaxiName("A"))];
        transport.Items["TAXI_PARKING"] = [(0, FacilityTestData.Parking())];

        return transport;
    }
}
