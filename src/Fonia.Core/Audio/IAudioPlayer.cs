using Fonia.Core.Speech;

namespace Fonia.Core.Audio;

/// <summary>The loudspeaker: plays what the controller says.</summary>
public interface IAudioPlayer : IAsyncDisposable
{
    /// <summary>
    /// Plays <paramref name="speech"/> and completes when it has been heard to the end. Cancelling cuts it short and
    /// throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task PlayAsync(SynthesizedSpeech speech, CancellationToken ct);
}
