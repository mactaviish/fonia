namespace Fonia.Sim.Mock.Telemetry;

/// <summary>A telemetry file is not valid: wrong header, unsupported version or a malformed snapshot.</summary>
public sealed class TelemetryFormatException : Exception
{
    public TelemetryFormatException(string message, int lineNumber, Exception? innerException = null)
        : base($"Invalid telemetry file at line {lineNumber}: {message}", innerException)
    {
        LineNumber = lineNumber;
    }

    /// <summary>1-based line of the file where the problem was found.</summary>
    public int LineNumber { get; }
}
