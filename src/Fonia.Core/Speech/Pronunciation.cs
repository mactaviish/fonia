using System.Text;

namespace Fonia.Core.Speech;

/// <summary>
/// How the controller's words are said aloud, kept apart from how they are written: the text of the phraseology
/// ("PAPA ROMEO ALFA BRAVO CHARLIE") stays as the manual has it, and this layer gives the voice a Brazilian spelling
/// of the words that Portuguese spelling rules would read wrongly ("romeo" as "romio", "pushback" as "puchebaque").
/// Heard on the voice model: without it "Charlie" came out "charLIE" and "pushback" "pushBAQUI".
/// </summary>
public static class Pronunciation
{
    // The phonetic alphabet as a Brazilian says it, and the English words the radio uses. The spelling is Portuguese
    // on purpose: accents mark the stress and the vowel, and PortuguesePhonemizer reads it with its ordinary rules.
    private static readonly Dictionary<string, string> Respellings = new(StringComparer.Ordinal)
    {
        ["delta"] = "délta",
        ["echo"] = "éco",
        ["foxtrot"] = "fóxtroti",
        ["golf"] = "gólfi",
        ["hotel"] = "hotél",
        ["juliett"] = "juliéte",
        ["mike"] = "máique",
        ["november"] = "november",
        ["quebec"] = "kebéque",
        ["romeo"] = "romeu",
        ["sierra"] = "siérra",
        ["uniform"] = "iuniform",
        ["victor"] = "víctor",
        ["whiskey"] = "uísque",
        ["x-ray"] = "écs rei",
        ["yankee"] = "yanque",
        ["charlie"] = "tchárli",
        ["pushback"] = "púchibéqui",
        ["qnh"] = "quê ene agá",
    };

    /// <summary>The text with each word that needs it replaced by its Brazilian spelling. Case and punctuation are kept apart from the word.</summary>
    public static string Respell(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new StringBuilder(text.Length);
        var word = new StringBuilder();

        void Flush()
        {
            if (word.Length == 0)
            {
                return;
            }

            var key = word.ToString().ToLowerInvariant();
            result.Append(Respellings.TryGetValue(key, out var spoken) ? spoken : word.ToString());
            word.Clear();
        }

        foreach (var c in text)
        {
            if (char.IsLetter(c) || (c == '-' && word.Length > 0))
            {
                word.Append(c);
            }
            else
            {
                Flush();
                result.Append(c);
            }
        }

        Flush();
        return result.ToString();
    }

    /// <summary>What the voice model is given for a sentence of the controller: respelled, then turned into phonemes.</summary>
    public static string ToPhonemes(string text) => PortuguesePhonemizer.Phonemize(Respell(text));
}
