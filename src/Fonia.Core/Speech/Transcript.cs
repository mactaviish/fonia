namespace Fonia.Core.Speech;

/// <summary>What the recognizer wrote down for one transmission.</summary>
/// <param name="Text">The text, trimmed; empty when nothing intelligible was heard.</param>
/// <param name="AudioDuration">How long the transmission lasted.</param>
/// <param name="Elapsed">How long the recognition took (what the pilot waits for).</param>
public sealed record Transcript(string Text, TimeSpan AudioDuration, TimeSpan Elapsed);
