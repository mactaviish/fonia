using Fonia.Core.Nlu;
using Fonia.Speech;

namespace Fonia.App;

/// <summary>The speech-to-text command, wired to the real recognizer. Only the composition root calls it.</summary>
internal static class SttCommands
{
    public static async Task<int> TranscribeAsync(AppOptions options, TextReader input, TextWriter output, TextWriter error, bool interactive, CancellationToken ct)
    {
        if (!await ModelCommands.EnsureAsync(ModelNeeds.ForTranscribe(options), input, output, error, interactive, ct))
        {
            return 1;
        }

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

        await using var transcriber = new WhisperTranscriber(new WhisperOptions(modelPath, options.SttGpu, Telephony: Companies.From(options)));

        return await new TranscribeCommand(transcriber, new RuleBasedNlu(Companies.From(options)), output, error).RunAsync(options.TranscribePath!, options.Callsign, ct);
    }
}
