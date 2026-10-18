namespace Fonia.Core.Speech;

/// <summary>Text to speech, locally. It says what it is given: the text comes from the phraseology, never from here.</summary>
public interface ISynthesizer : IAsyncDisposable
{
    /// <summary>
    /// Speaks one utterance with <paramref name="voice"/>. Calls are served one at a time, in order. The first call may
    /// take longer: it loads the model.
    /// </summary>
    /// <param name="text">What the controller says, as the phraseology wrote it ("PAPA ROMEO ALFA BRAVO CHARLIE, prossiga.").</param>
    /// <param name="pace">How fast to speak relative to the usual: 1 is the usual, below 1 is slower (the ATIS is read slowly, to be copied).</param>
    Task<SynthesizedSpeech> SynthesizeAsync(string text, Voice voice, CancellationToken ct, double pace = 1.0);
}
