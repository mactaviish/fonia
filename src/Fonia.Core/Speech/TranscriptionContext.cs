namespace Fonia.Core.Speech;

/// <summary>
/// What is known about the flight before the pilot speaks, so the recognizer can be told which words to expect.
/// Everything is optional.
/// </summary>
/// <param name="Callsign">The aircraft's callsign ("PR-ABC"); it goes to the recognizer as it is spoken.</param>
/// <param name="Station">Who answers, as it is called on the radio ("Solo Curitiba").</param>
public sealed record TranscriptionContext(string? Callsign = null, string? Station = null)
{
    public static TranscriptionContext Empty { get; } = new();
}
