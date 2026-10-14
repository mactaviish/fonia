namespace Fonia.Core.Speech;

public enum VoiceGender
{
    Feminine,
    Masculine,
}

/// <summary>A voice of the speech synthesizer.</summary>
/// <param name="Id">The name the voice model knows it by ("pf_dora").</param>
/// <param name="Name">The name shown to the user.</param>
public sealed record Voice(string Id, string Name, VoiceGender Gender);

/// <summary>The voices offered to the user: only the ones the maintainer listened to and approved.</summary>
public static class VoiceCatalog
{
    // "pm_santa" is in the model too and is left out on purpose: rejected on listening (docs/decisions/speech.md).
    private static readonly Voice[] Voices =
    [
        new("pf_dora", "Dora", VoiceGender.Feminine),
        new("pm_alex", "Alex", VoiceGender.Masculine),
    ];

    public static IReadOnlyList<Voice> Default => Voices;

    /// <summary>The voice with this id (case does not matter), or null when it is not one of the offered voices.</summary>
    public static Voice? Find(string? id) =>
        id is null ? null : Voices.FirstOrDefault(v => string.Equals(v.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
