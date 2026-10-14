using System.Globalization;
using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Speech;

namespace Fonia.App;

/// <summary>
/// <c>--transcribe</c>: reads a recording, writes down what was said and shows what the NLU makes of it. It is the
/// step after <c>--mic-test</c> and before the live voice loop, and the way to check the recognizer on one's own voice.
/// </summary>
internal sealed class TranscribeCommand(ITranscriber transcriber, INlu nlu, TextWriter output, TextWriter error)
{
    /// <returns>0 when the recording was transcribed (even if nothing was recognized), 1 when it could not be.</returns>
    public async Task<int> RunAsync(string path, string? callsign, CancellationToken ct)
    {
        byte[] bytes;

        try
        {
            bytes = await File.ReadAllBytesAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Não foi possível ler '{path}': {ex.Message}");
            return 1;
        }

        RecordedSpeech speech;

        try
        {
            speech = WavDecoder.Decode(bytes);
        }
        catch (InvalidDataException ex)
        {
            error.WriteLine($"'{path}' não é um WAV utilizável: {ex.Message}");
            return 1;
        }

        if (speech.Format != AudioFormat.Speech)
        {
            error.WriteLine(
                $"O áudio tem {speech.Format.SampleRate} Hz, {speech.Format.Channels} canal(is) e {speech.Format.BitsPerSample} bits; "
                + "o reconhecimento precisa de 16 kHz, mono, 16 bits (ex.: ffmpeg -i entrada.wav -ar 16000 -ac 1 -c:a pcm_s16le saida.wav).");
            return 1;
        }

        output.WriteLine("Reconhecendo (na primeira vez o modelo é carregado, o que leva alguns segundos)...");

        Transcript transcript;

        try
        {
            transcript = await transcriber.TranscribeAsync(speech, new TranscriptionContext(callsign), ct);
        }
        catch (FileNotFoundException ex)
        {
            error.WriteLine(ex.Message);
            return 1;
        }

        output.WriteLine($"Fala: {Seconds(transcript.AudioDuration)} s | reconhecimento: {Seconds(transcript.Elapsed)} s");

        if (transcript.Text.Length == 0)
        {
            output.WriteLine("Texto: (nada reconhecido)");
            return 0;
        }

        output.WriteLine($"Texto: {transcript.Text}");

        var flight = new FlightContext(Normalize(callsign), FlightPhase.Parked, null, null);
        var intent = nlu.Interpret(transcript.Text, flight);
        output.WriteLine($"Entendido: {intent.GetType().Name} (indicativo {intent.Callsign ?? "não ouvido"})");

        return 0;
    }

    private static string Normalize(string? callsign) =>
        callsign is null ? string.Empty : new string(callsign.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Seconds(TimeSpan duration) =>
        duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
}
