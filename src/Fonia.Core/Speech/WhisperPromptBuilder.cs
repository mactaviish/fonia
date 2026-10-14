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
        "Comunicação de rádio aeronáutica em português. Solo, Torre, pushback, acionamento, táxi, pista, ponto de espera, QNH.";

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
        return prompt.ToString();
    }

    /// <summary>"PAPA ROMEO" to "Papa Romeo": prompts are written like text, not shouted like the manual.</summary>
    private static string Capitalize(string spoken) =>
        string.Join(' ', spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Concat(word[..1].ToUpperInvariant(), word[1..].ToLower(CultureInfo.InvariantCulture))));
}
