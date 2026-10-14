using Fonia.Core.Nlu;
using Fonia.Speech;

namespace Fonia.App;

/// <summary>The speech-to-text command, wired to the real recognizer. Only the composition root calls it.</summary>
internal static class SttCommands
{
    public static async Task<int> TranscribeAsync(AppOptions options, TextWriter output, TextWriter error, CancellationToken ct)
    {
        string modelPath;

        try
        {
            modelPath = WhisperModelLocator.Resolve(options.SttDirectory, options.SttModel);
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine($"fonia: {ex.Message}");
            return 1;
        }
        catch (ArgumentException ex)
        {
            error.WriteLine($"fonia: {ex.Message}");
            return 2;
        }

        await using var transcriber = new WhisperTranscriber(new WhisperOptions(modelPath, options.SttGpu));

        return await new TranscribeCommand(transcriber, new RuleBasedNlu(), output, error).RunAsync(options.TranscribePath!, options.Callsign, ct);
    }
}
