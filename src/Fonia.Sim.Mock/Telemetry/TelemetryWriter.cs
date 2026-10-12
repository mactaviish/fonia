using System.Text;
using System.Text.Json;
using Fonia.Core.Sim;

namespace Fonia.Sim.Mock.Telemetry;

/// <summary>
/// Writes a telemetry recording: UTF-8 (no BOM), <c>\n</c> line endings, a header line followed by one
/// JSON line per <see cref="AircraftState"/>. Every line is flushed as soon as it is written, so a
/// recording interrupted by a crash still holds everything up to the last complete line.
/// </summary>
public sealed class TelemetryWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;

    private TelemetryWriter(StreamWriter writer)
    {
        _writer = writer;
    }

    /// <summary>Creates a writer and writes the header line.</summary>
    /// <param name="destination">Where to write. Closed on disposal unless <paramref name="leaveOpen"/> is set.</param>
    public static async Task<TelemetryWriter> CreateAsync(Stream destination, bool leaveOpen, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: leaveOpen)
        {
            NewLine = "\n",
        };

        var result = new TelemetryWriter(writer);
        var header = new TelemetryHeader(TelemetryFile.Format, TelemetryFile.CurrentVersion);
        await result.WriteLineAsync(JsonSerializer.Serialize(header, TelemetryJsonContext.Default.TelemetryHeader), ct);
        return result;
    }

    public Task WriteAsync(AircraftState state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);

        return WriteLineAsync(JsonSerializer.Serialize(state, TelemetryJsonContext.Default.AircraftState), ct);
    }

    public ValueTask DisposeAsync()
    {
        return _writer.DisposeAsync();
    }

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _writer.WriteLineAsync(line.AsMemory(), ct);
        await _writer.FlushAsync(ct);
    }
}
