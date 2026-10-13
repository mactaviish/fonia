using Fonia.Core.Atc;

namespace Fonia.Core.Phraseology;

/// <summary>Whether an <see cref="Utterance"/> goes out on the radio or is only a message of the app.</summary>
public enum UtteranceKind
{
    /// <summary>The controller speaks it on the radio (it is voiced by the TTS later).</summary>
    Radio,

    /// <summary>The app tells the user something nobody would say on the radio (no one answers at this airport).</summary>
    Notice,
}

/// <summary>What the controller says, as text. Written from templates, never by a language model.</summary>
public sealed record Utterance(string Text, UtteranceKind Kind = UtteranceKind.Radio);

/// <summary>Who the controller is talking to and where, for the words that depend on it.</summary>
/// <param name="Callsign">The aircraft's normalized callsign ("PRABC").</param>
public sealed record PhraseContext(string Callsign, AirportContext Airport);

/// <summary>Turns what the ATC engine decided into what the controller says, in one language.</summary>
public interface IPhraseology
{
    /// <summary>The words for <paramref name="instruction"/>, or null when the controller says nothing.</summary>
    Utterance? Speak(AtcInstruction instruction, PhraseContext context);

    /// <summary>How the unit that answers at this airport is called, e.g. "SOLO Curitiba" (MCA 100-16 Art. 37).</summary>
    string UnitName(AirportContext airport);
}
