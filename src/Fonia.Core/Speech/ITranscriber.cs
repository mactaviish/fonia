using Fonia.Core.Audio;

namespace Fonia.Core.Speech;

/// <summary>Speech to text, locally. The text goes on to the NLU, which tolerates what a recognizer gets wrong.</summary>
public interface ITranscriber : IAsyncDisposable
{
    /// <summary>
    /// Writes down one transmission. Calls are served one at a time, in order. The first call may take
    /// longer: it loads the model.
    /// </summary>
    /// <param name="speech">Audio in <see cref="AudioFormat.Speech"/>.</param>
    /// <param name="context">What is known about the flight, to guide the recognizer.</param>
    /// <param name="ct">Cancels the wait; a recognition already running may finish in the background.</param>
    Task<Transcript> TranscribeAsync(RecordedSpeech speech, TranscriptionContext context, CancellationToken ct);
}
