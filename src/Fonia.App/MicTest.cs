using System.Globalization;
using Fonia.Core.Audio;

namespace Fonia.App;

/// <summary>
/// <c>--mic-test</c>: the pilot holds the push-to-talk, speaks and releases; the first transmission is saved as a WAV
/// file and the run ends. It proves that the microphone, the key and the recording limits work before any speech
/// recognizer is involved, and leaves a file to listen to.
/// </summary>
internal sealed class MicTest(IAudioCapture capture, IPushToTalk pushToTalk, string keyName, TextWriter output, TextWriter error)
{
    /// <returns>0 when it ends normally (including cancelled), 1 when the file could not be written.</returns>
    public async Task<int> RunAsync(string path, CancellationToken ct)
    {
        using var recorder = new SpeechRecorder(capture, pushToTalk);
        var recorded = new TaskCompletionSource<RecordedSpeech>(TaskCreationOptions.RunContinuationsAsynchronously);

        recorder.SpeechRecorded += (_, speech) => recorded.TrySetResult(speech);
        recorder.SpeechDiscarded += (_, discarded) =>
            error.WriteLine($"Fala curta demais ({Seconds(discarded.Duration)} s): segure {keyName} enquanto fala e tente de novo.");

        recorder.Start();
        output.WriteLine($"Teste do microfone. Segure {keyName}, fale e solte. Ctrl+C cancela.");

        RecordedSpeech speech;

        try
        {
            speech = await recorded.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            output.WriteLine("Cancelado.");
            return 0;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(path, WavEncoder.Encode(speech), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Não foi possível gravar '{path}': {ex.Message}");
            return 1;
        }

        output.WriteLine($"Gravado {Seconds(speech.Duration)} s em {path}");
        return 0;
    }

    private static string Seconds(TimeSpan duration) =>
        duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
}
