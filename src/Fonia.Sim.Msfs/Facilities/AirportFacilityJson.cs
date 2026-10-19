using System.Text.Json;

namespace Fonia.Sim.Msfs;

/// <summary>Who and when captured a dump: it goes into the file so that a dump brought back later says where it comes from.</summary>
/// <param name="IsMsfs2024">True for MSFS 2024, false for 2020, null when not known.</param>
public sealed record FacilityCapture(string Tool, DateTimeOffset At, bool? IsMsfs2024);

/// <summary>The raw dump of an airport as a JSON file: what the simulator said, as it said it (camelCase, indented).</summary>
public static class AirportFacilityJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Write(AirportFacility facility, FacilityCapture capture)
    {
        ArgumentNullException.ThrowIfNull(facility);
        ArgumentNullException.ThrowIfNull(capture);

        return JsonSerializer.Serialize(new Dump(capture, facility), Options);
    }

    private sealed record Dump(FacilityCapture Capture, AirportFacility Facility);
}
