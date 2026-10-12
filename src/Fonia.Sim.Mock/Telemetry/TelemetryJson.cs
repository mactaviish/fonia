using System.Text.Json.Serialization;
using Fonia.Core.Sim;

namespace Fonia.Sim.Mock.Telemetry;

/// <summary>File format constants shared by the writer and the reader.</summary>
internal static class TelemetryFile
{
    public const string Format = "fonia-telemetry";

    /// <summary>The only version this build reads and writes. Bump it when a snapshot change is not additive.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>First line of every telemetry file.</summary>
internal sealed record TelemetryHeader(string Format, int Version);

// Source-generated serialization: no reflection at run time, friendly to trimmed/single-file publishing.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = [typeof(FrequencyJsonConverter)],
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AircraftState))]
[JsonSerializable(typeof(TelemetryHeader))]
internal sealed partial class TelemetryJsonContext : JsonSerializerContext;
