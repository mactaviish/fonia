namespace Fonia.Core.Speech;

public enum VoiceGender
{
    Feminine,
    Masculine,
}

/// <summary>A voice of the speech synthesizer.</summary>
/// <param name="Id">The name the voice model knows it by ("pf_dora").</param>
/// <param name="Name">The name shown to the user.</param>
/// <param name="Language">"pt-BR" for the controller; "en" for the voice that reads the ATIS in English.</param>
public sealed record Voice(string Id, string Name, VoiceGender Gender, string Language = "pt-BR");

/// <summary>The voices offered to the user: only the ones the maintainer listened to and approved.</summary>
public static class VoiceCatalog
{
    // "pm_santa" is in the model too and is left out on purpose: rejected on listening (docs/decisions/speech.md).
    private static readonly Voice[] Voices =
    [
        new("pf_dora", "Dora", VoiceGender.Feminine),
        new("pm_alex", "Alex", VoiceGender.Masculine),
    ];

    // The English half of the ATIS: not offered to the user as the controller's voice. TODO: to be approved by listening, like the others.
    private static readonly Voice EnglishFeminine = new("af_heart", "Heart", VoiceGender.Feminine, "en");

    public static IReadOnlyList<Voice> Default => Voices;

    /// <summary>The voice of the ATIS of every airport, in Portuguese: always the same, feminine, as a recording is.</summary>
    public static Voice AtisPortuguese => Voices[0];

    /// <summary>The voice of the ATIS in English: feminine too, so that the two halves are one voice.</summary>
    public static Voice AtisEnglish => EnglishFeminine;

    /// <summary>The voice with this id (case does not matter), or null when it is not one of the offered voices.</summary>
    public static Voice? Find(string? id) =>
        id is null ? null : Voices.FirstOrDefault(v => string.Equals(v.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
