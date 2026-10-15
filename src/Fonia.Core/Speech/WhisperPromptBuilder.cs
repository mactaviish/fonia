using System.Globalization;
using System.Text;
using Fonia.Core.Phraseology;

namespace Fonia.Core.Speech;

/// <summary>
/// Builds the text that primes the recognizer with the words of this radio: aviation vocabulary, the station and the
/// callsign spelled the way the pilot says it. Measured on real recordings, this took the share of requests the NLU
/// understood from 30% to 100% (docs/decisions/speech.md): without it the recognizer writes "Paparromeu" for
/// "Papa Romeo".
/// </summary>
public static class WhisperPromptBuilder
{
    private const string Vocabulary =
        "Comunicação de rádio aeronáutica em português. Solo, Torre, pushback, acionamento, táxi, pista, ponto de espera, alinhe e mantenha, decolagem, pronto para partida, alinhado, circuito de tráfego, perna do vento, perna base, final, pouso, arremetendo, QNH.";

    private const string RunwayExample = "Pista uno cinco.";

    public static string Build(TranscriptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var prompt = new StringBuilder(Vocabulary);

        if (!string.IsNullOrWhiteSpace(context.Station))
        {
            prompt.Append(' ').Append(context.Station.Trim().TrimEnd('.')).Append('.');
        }

        if (context.Callsign is { } callsign && callsign.Any(char.IsAsciiLetterOrDigit))
        {
            prompt.Append(' ').Append(Capitalize(SpokenText.Callsign(callsign))).Append('.');
        }

        prompt.Append(' ').Append(RunwayExample);

        // The pilot is most likely reading back what the controller just said: with those words in the prompt the recognizer writes them
        // right ("decolagem", not "de colagem"). It goes last, the closest to the speech.
        if (!string.IsNullOrWhiteSpace(context.LastTransmission))
        {
            prompt.Append(" Controlador: ").Append(AsText(context.LastTransmission.Trim()));
        }

        return prompt.ToString();
    }

    // "PAPA ROMEO, PISTA UNO CINCO, alinhe e mantenha." to "Papa Romeo, Pista Uno Cinco, alinhe e mantenha.": the words the manual
    // shouts (numbers, letters, callsigns) are written like text.
    private static string AsText(string spoken) =>
        string.Join(' ', spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => IsShouted(word) ? Capitalize(word) : word));

    private static bool IsShouted(string word)
    {
        var letters = word.Where(char.IsLetter).ToList();

        return letters.Count > 1 && letters.All(char.IsUpper);
    }

    /// <summary>"PAPA ROMEO" to "Papa Romeo": prompts are written like text, not shouted like the manual.</summary>
    private static string Capitalize(string spoken) =>
        string.Join(' ', spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Concat(word[..1].ToUpperInvariant(), word[1..].ToLower(CultureInfo.InvariantCulture))));
}
