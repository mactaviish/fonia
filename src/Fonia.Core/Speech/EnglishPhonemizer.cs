using System.Text;

namespace Fonia.Core.Speech;

/// <summary>
/// The English the app speaks is a closed list of words (the ATIS in English): each one has its phonemes written down here, in the symbols
/// the voice model was trained on (American English, with A I O W Y for the diphthongs). There is no general English phonemizer on purpose: the
/// usual one (espeak-ng) is GPL. A word that is not in the list is a name (the airport's) and is read by the Portuguese rules.
/// </summary>
public static class EnglishPhonemizer
{
    // The numbers are said the ICAO way (Doc 4444, 12.2): "tree", "fife", "niner", "tousand".
    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        ["zero"] = "zˈiɹO",
        ["one"] = "wˈʌn",
        ["two"] = "tˈu",
        ["three"] = "tɹˈi",
        ["four"] = "fˈɔɹ",
        ["five"] = "fˈIf",
        ["six"] = "sˈɪks",
        ["seven"] = "sˈɛvən",
        ["eight"] = "ˈAt",
        ["nine"] = "nˈInɚ",
        ["thousand"] = "tˈWzənd",
        ["hundred"] = "hˈʌndɹəd",
        ["information"] = "ˌɪnfɚmˈAʃən",
        ["time"] = "tˈIm",
        ["runway"] = "ɹˈʌnwˌA",
        ["left"] = "lˈɛft",
        ["right"] = "ɹˈIt",
        ["centre"] = "sˈɛntɚ",
        ["in"] = "ɪn",
        ["use"] = "jˈus",
        ["wind"] = "wˈɪnd",
        ["calm"] = "kˈɑm",
        ["degrees"] = "dəɡɹˈiz",
        ["knots"] = "nˈɑts",
        ["gusting"] = "ɡˈʌstɪŋ",
        ["visibility"] = "vˌɪzəbˈɪləɾi",
        ["meters"] = "mˈiɾɚz",
        ["kilometers"] = "kɪlˈɑmɪɾɚz",
        ["more"] = "mˈɔɹ",
        ["than"] = "ðˈæn",
        ["ten"] = "tˈɛn",
        ["thunderstorm"] = "θˈʌndɚstˌɔɹm",
        ["with"] = "wɪð",
        ["rain"] = "ɹˈAn",
        ["shower"] = "ʃˈWɚ",
        ["of"] = "ʌv",
        ["drizzle"] = "dɹˈɪzəl",
        ["hail"] = "hˈAl",
        ["fog"] = "fˈɑɡ",
        ["mist"] = "mˈɪst",
        ["haze"] = "hˈAz",
        ["smoke"] = "smˈOk",
        ["light"] = "lˈIt",
        ["heavy"] = "hˈɛvi",
        ["oktas"] = "ˈɑktəz",
        ["feet"] = "fˈit",
        ["cumulonimbus"] = "kjˌumjəlOnˈɪmbəs",
        ["temperature"] = "tˈɛmpɹəʧɚ",
        ["minus"] = "mˈInəs",
        ["qnh"] = "kjˈu ˈɛn ˈAʧ",
        ["advise"] = "ədvˈIz",
        ["on"] = "ˈɑn",
        ["initial"] = "ɪnˈɪʃəl",
        ["contact"] = "kˈɑntˌækt",
        ["you"] = "jˈu",
        ["have"] = "hˈæv",

        // The phonetic alphabet (ICAO), for the designator.
        ["alfa"] = "ˈælfə",
        ["bravo"] = "bɹˈɑvO",
        ["charlie"] = "ʧˈɑɹli",
        ["delta"] = "dˈɛltə",
        ["echo"] = "ˈɛkO",
        ["foxtrot"] = "fˈɑkstɹˌɑt",
        ["golf"] = "ɡˈɔlf",
        ["hotel"] = "hOtˈɛl",
        ["india"] = "ˈɪndiə",
        ["juliett"] = "ʤˈuliˌɛt",
        ["kilo"] = "kˈilO",
        ["lima"] = "lˈilə",
        ["mike"] = "mˈIk",
        ["november"] = "nOvˈɛmbɚ",
        ["oscar"] = "ˈɑskɚ",
        ["papa"] = "pəpˈɑ",
        ["quebec"] = "kwəbˈɛk",
        ["romeo"] = "ɹˈOmiˌO",
        ["sierra"] = "siˈɛɹə",
        ["tango"] = "tˈæŋɡO",
        ["uniform"] = "jˈunəfˌɔɹm",
        ["victor"] = "vˈɪktɚ",
        ["whiskey"] = "wˈɪski",
        ["x-ray"] = "ˈɛksɹˌA",
        ["yankee"] = "jˈæŋki",
        ["zulu"] = "zˈulu",
    };

    /// <summary>Whether the word is one the list knows (the words of the ATIS in English, the digits and the phonetic alphabet).</summary>
    public static bool Knows(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return Words.ContainsKey(word.ToLowerInvariant());
    }

    /// <summary>The phonemes of a text: the words of the list as written above, any other word by the Portuguese rules, the punctuation kept.</summary>
    public static string Phonemize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new StringBuilder();
        var word = new StringBuilder();

        void Flush()
        {
            if (word.Length == 0)
            {
                return;
            }

            var spelled = word.ToString();
            word.Clear();

            if (result.Length > 0 && result[^1] != ' ')
            {
                result.Append(' ');
            }

            result.Append(Words.TryGetValue(spelled.ToLowerInvariant(), out var phonemes) ? phonemes : Pronunciation.ToPhonemes(spelled));
        }

        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '-')
            {
                word.Append(c);
            }
            else
            {
                Flush();

                if (c is '.' or ',' or ';' or ':' or '!' or '?')
                {
                    result.Append(c);
                }
            }
        }

        Flush();
        return result.ToString();
    }
}
