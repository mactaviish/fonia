using System.Globalization;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;

namespace Fonia.App;

/// <summary>
/// Shows the voice loop on the console: what the recognizer heard, what the controller answers and how long each stage
/// took. The voice is the answer; the text is there so that the user can see what was understood.
/// </summary>
internal sealed class ConsoleVoiceObserver(TextWriter output, TextWriter error, string station) : IVoiceLoopObserver
{
    private readonly Lock _gate = new();

    public void OnHeard(string text)
    {
        lock (_gate)
        {
            output.WriteLine(string.IsNullOrWhiteSpace(text) ? "Você disse: (nada reconhecido)" : $"Você disse: \"{text.Trim()}\"");
        }
    }

    public void OnReplied(ExchangeResult result)
    {
        lock (_gate)
        {
            switch (result.Reply)
            {
                case null:
                    output.WriteLine("(sem resposta do controlador)");
                    break;
                case { Kind: UtteranceKind.Notice } notice:
                    output.WriteLine($"[aviso] {notice.Text}");
                    break;
                case var reply:
                    output.WriteLine($"{(result.Station.Length > 0 ? result.Station : station)}: {reply.Text}");
                    break;
            }

            output.WriteLine(Describe(result));
        }
    }

    public void OnChannelBusy()
    {
        lock (_gate)
        {
            output.WriteLine("(canal ocupado: espere o controlador terminar de falar)");
        }
    }

    public void OnTooShort(TimeSpan held)
    {
        lock (_gate)
        {
            output.WriteLine($"(fala curta demais ({Format(held)}): segure a tecla enquanto fala)");
        }
    }

    public void OnFailed(Exception exception)
    {
        lock (_gate)
        {
            error.WriteLine($"fonia: falha na troca de voz: {exception.Message}");
        }
    }

    private static string Describe(ExchangeResult result)
    {
        var timings = result.Timings;
        var stages = $"reconhecimento {Format(timings.Transcription)} | entendimento {Format(timings.Understanding)}";

        if (result.Speech is not null)
        {
            stages += $" | voz {Format(timings.Synthesis)} | rádio {Format(timings.RadioEffect)}";
        }

        return $"  tempos: {stages} | até o áudio {Format(timings.Total)}";
    }

    // Under a second in milliseconds, which is what matters for a fast stage, otherwise in seconds.
    private static string Format(TimeSpan time) => time < TimeSpan.FromSeconds(1)
        ? $"{(int)Math.Round(time.TotalMilliseconds)} ms"
        : time.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " s";
}
