using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fonia.Core.Sim;

namespace Fonia.Core.Airports;

/// <summary>
/// The JSON of an <see cref="AirportLayout"/>, the way it is kept in <c>%LOCALAPPDATA%\Fonia\layouts</c>. It carries the version of the format;
/// both reading and writing check that the graph holds together, so that a half-broken map is never used to speak a route.
/// </summary>
public static class AirportLayoutJson
{
    /// <summary>The version of the format this code reads and writes.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new FrequencyConverter() },
    };

    /// <exception cref="AirportLayoutFormatException">The layout would not be read back.</exception>
    public static string Write(AirportLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        Validate(layout);

        var node = JsonSerializer.SerializeToNode(layout, Options)!.AsObject();
        var document = new JsonObject { ["version"] = CurrentVersion };

        foreach (var (name, value) in node.ToList())
        {
            node.Remove(name);
            document[name] = value;
        }

        return document.ToJsonString(Options);
    }

    /// <exception cref="AirportLayoutFormatException">The text is not a layout of a version this code reads, or the layout is not sound.</exception>
    public static AirportLayout Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        AirportLayout? layout;

        try
        {
            var root = JsonNode.Parse(json) as JsonObject ?? throw new AirportLayoutFormatException("The layout is not a JSON object.");

            if (root["version"]?.GetValue<int>() != CurrentVersion)
            {
                throw new AirportLayoutFormatException($"The layout has version {root["version"]?.ToJsonString() ?? "none"}; this code reads version {CurrentVersion}.");
            }

            layout = root.Deserialize<AirportLayout>(Options);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new AirportLayoutFormatException($"The layout could not be read: {e.Message}", e);
        }

        if (layout is null)
        {
            throw new AirportLayoutFormatException("The layout is empty.");
        }

        Validate(layout);

        return layout;
    }

    // What makes a graph worth speaking a route from: every reference goes somewhere and no position is off the Earth.
    private static void Validate(AirportLayout layout)
    {
        if (string.IsNullOrWhiteSpace(layout.Ident))
        {
            throw new AirportLayoutFormatException("The layout has no ident.");
        }

        if (layout.Runways is null || layout.Nodes is null || layout.Edges is null || layout.Parkings is null || layout.Frequencies is null)
        {
            throw new AirportLayoutFormatException($"The layout of {layout.Ident} is missing a list (runways, nodes, edges, parkings or frequencies).");
        }

        foreach (var runway in layout.Runways)
        {
            if (runway.Low is null || runway.High is null || string.IsNullOrWhiteSpace(runway.Low.Designator) || string.IsNullOrWhiteSpace(runway.High.Designator))
            {
                throw new AirportLayoutFormatException($"A runway of {layout.Ident} has no ends.");
            }

            if (!(runway.LengthMeters > 0) || !(runway.WidthMeters > 0))
            {
                throw new AirportLayoutFormatException($"Runway {runway.Name} of {layout.Ident} has no length or width.");
            }

            CheckPosition(layout, runway.Low.Position, $"runway end {runway.Low.Designator}");
            CheckPosition(layout, runway.High.Position, $"runway end {runway.High.Designator}");
        }

        var ids = new HashSet<int>();

        foreach (var node in layout.Nodes)
        {
            if (!ids.Add(node.Id))
            {
                throw new AirportLayoutFormatException($"The layout of {layout.Ident} has two nodes with id {node.Id}.");
            }

            CheckPosition(layout, node.Position, $"node {node.Id}");
        }

        foreach (var edge in layout.Edges)
        {
            foreach (var end in new[] { edge.From, edge.To })
            {
                if (!ids.Contains(end))
                {
                    throw new AirportLayoutFormatException($"An edge of {layout.Ident} goes to node {end}, which does not exist.");
                }
            }

            if (edge.From == edge.To)
            {
                throw new AirportLayoutFormatException($"An edge of {layout.Ident} goes from node {edge.From} to itself.");
            }

            if (edge.Kind == TaxiEdgeKind.Runway && string.IsNullOrWhiteSpace(edge.Runway))
            {
                throw new AirportLayoutFormatException($"A runway edge of {layout.Ident} (node {edge.From} to {edge.To}) does not say which runway it is.");
            }
        }

        foreach (var parking in layout.Parkings)
        {
            CheckPosition(layout, parking.Position, $"parking {parking.Name}");

            if (parking.Node is { } entry && !ids.Contains(entry))
            {
                throw new AirportLayoutFormatException($"Parking {parking.Name} of {layout.Ident} enters the graph at node {entry}, which does not exist.");
            }
        }
    }

    private static void CheckPosition(AirportLayout layout, GeoPoint? position, string what)
    {
        if (position is null || !(position.LatitudeDegrees is >= -90 and <= 90) || !(position.LongitudeDegrees is >= -180 and <= 180))
        {
            throw new AirportLayoutFormatException($"The {what} of {layout.Ident} is not a position on the Earth.");
        }
    }

    // A frequency is a whole number of kHz in the file, as it is in memory.
    private sealed class FrequencyConverter : JsonConverter<Frequency>
    {
        public override Frequency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var kHz = reader.GetInt32();

            return kHz > 0 ? new Frequency(kHz) : throw new JsonException($"{kHz} kHz is not a frequency.");
        }

        public override void Write(Utf8JsonWriter writer, Frequency value, JsonSerializerOptions options) => writer.WriteNumberValue(value.KHz);
    }
}
