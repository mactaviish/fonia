using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Fonia.Core.Sim;

namespace Fonia.Sim.Mock.Telemetry;

/// <summary>Reads a recording made by <see cref="TelemetryWriter"/>.</summary>
public static class TelemetryReader
{
    /// <summary>
    /// Streams the snapshots of a recording in file order. The header is checked first and a bad file fails
    /// with <see cref="TelemetryFormatException"/> naming the line; snapshots before the problem are still
    /// yielded. Blank lines and properties added by later versions are ignored. The stream is left open.
    /// </summary>
    public static async IAsyncEnumerable<AircraftState> ReadAsync(
        Stream source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var reader = new StreamReader(source, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var lineNumber = 0;
        var headerSeen = false;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!headerSeen)
            {
                CheckHeader(line, lineNumber);
                headerSeen = true;
                continue;
            }

            yield return ParseSnapshot(line, lineNumber);
        }

        if (!headerSeen)
        {
            throw new TelemetryFormatException("the file is empty (missing header).", Math.Max(1, lineNumber));
        }
    }

    private static void CheckHeader(string line, int lineNumber)
    {
        TelemetryHeader? header;

        try
        {
            header = JsonSerializer.Deserialize(line, TelemetryJsonContext.Default.TelemetryHeader);
        }
        catch (JsonException ex)
        {
            throw new TelemetryFormatException("the first line is not a telemetry header.", lineNumber, ex);
        }

        if (header is null || header.Format != TelemetryFile.Format)
        {
            throw new TelemetryFormatException($"the first line is not a '{TelemetryFile.Format}' header.", lineNumber);
        }

        if (header.Version != TelemetryFile.CurrentVersion)
        {
            var hint = header.Version > TelemetryFile.CurrentVersion ? " It was probably recorded by a newer Fonia." : string.Empty;
            throw new TelemetryFormatException(
                $"the file has version {header.Version} but this Fonia supports version {TelemetryFile.CurrentVersion} only.{hint}",
                lineNumber);
        }
    }

    private static AircraftState ParseSnapshot(string line, int lineNumber)
    {
        try
        {
            return JsonSerializer.Deserialize(line, TelemetryJsonContext.Default.AircraftState)
                ?? throw new TelemetryFormatException("the snapshot is empty.", lineNumber);
        }
        catch (JsonException ex)
        {
            throw new TelemetryFormatException($"the snapshot is malformed ({ex.Message}).", lineNumber, ex);
        }
    }
}
