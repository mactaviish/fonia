using System.Globalization;
using System.Text;

namespace Fonia.Core.Speech;

/// <summary>
/// Turns Brazilian Portuguese text into the phonemes the voice model reads (IPA, with <c>ˈ</c> before the stressed
/// vowel and <c>ˌ</c> before secondary ones), by rules written for this project: the usual spelling of the language
/// (accents, "ti" and "di", "lh", "nh", "ch", nasal vowels, the "l" that sounds like "u", the final "o" and "a" that
/// are reduced) plus a small list of exceptions. It exists so that speech does not depend on a GPL phonemizer.
/// </summary>
/// <remarks>
/// The output follows the conventions of the phoneme set the voice was trained on (final "a" as <c>æ</c>, final "o"
/// as <c>ʊ</c>, final "e" as <c>y</c>, "r" before a consonant as <c>ɾə</c>), not the textbook transcription: the model
/// speaks best what it was trained on. Foreign words are not spelled out here; give them a Brazilian spelling first
/// (<see cref="Pronunciation"/>). A word the rules get wrong is fixed with an entry in the exceptions, never in the rules.
/// </remarks>
public static class PortuguesePhonemizer
{
    // Words whose stressed "e" or "o" is open and that the spelling does not show ("sete" is sɛtʃi, "preto" is pretu).
    private static readonly HashSet<string> OpenStress = new(StringComparer.Ordinal)
    {
        "sete", "nove", "uniforme", "espera", "reporte", "controle", "viracopos", "fontenelle", "novo", "nova", "zero", "solo", "torre", "velho", "velha", "foz", "box", "alegre", "terra", "serra", "festa",
        "ovo", "osso", "forte", "morte", "corte", "sorte", "norte", "porta", "horta", "pedra", "perto", "certo", "erro", "ferro",
    };

    // Words that lean on the next one and carry no stress of their own.
    private static readonly HashSet<string> Clitics = new(StringComparer.Ordinal)
    {
        "a", "o", "as", "os", "de", "da", "do", "das", "dos", "e", "em", "no", "na", "nos", "nas", "ao", "aos", "que", "se", "por", "para", "pra", "um", "uma",
    };

    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["e"] = "i",
        ["a"] = "a",
        ["o"] = "ʊ",
        ["um"] = "ũŋ",
    };

    private const string Vowels = "aeiou";

    /// <summary>The phonemes of a text: words separated by spaces, and the punctuation that shapes the intonation kept.</summary>
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

            if (result.Length > 0 && result[^1] != ' ')
            {
                result.Append(' ');
            }

            result.Append(PhonemizeWord(word.ToString()));
            word.Clear();
        }

        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetter(c))
            {
                word.Append(c);
            }
            else if (c == '-' && word.Length > 0)
            {
                Flush();
            }
            else if (c is ',' or '.' or '!' or '?' or ':' or ';')
            {
                Flush();
                result.Append(c);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return result.ToString();
    }

    /// <summary>The phonemes of one word.</summary>
    public static string PhonemizeWord(string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);

        var lower = word.ToLowerInvariant();

        if (Exceptions.TryGetValue(lower, out var fixedPhonemes))
        {
            return fixedPhonemes.Normalize(NormalizationForm.FormD);
        }

        var letters = Spell(lower);
        var units = new Parser(letters).Parse();
        // The voice model's alphabet has the combining tilde, not the precomposed letters (õ, ũ).
        return Render(units, lower).Normalize(NormalizationForm.FormD);
    }

    // The letters with their accent marks apart: "á" is ('a', acute).
    private static List<Letter> Spell(string word)
    {
        var letters = new List<Letter>();

        foreach (var c in word.Normalize(NormalizationForm.FormD))
        {
            switch (c)
            {
                case '́':
                    Mark(letters, Accent.Acute);
                    break;
                case '̂':
                    Mark(letters, Accent.Circumflex);
                    break;
                case '̃':
                    Mark(letters, Accent.Tilde);
                    break;
                case '̧':
                    Mark(letters, Accent.Cedilla);
                    break;
                case '̀' or '̈':
                    break;
                default:
                    if (c is >= 'a' and <= 'z')
                    {
                        letters.Add(new Letter(c, Accent.None));
                    }

                    break;
            }
        }

        return letters;
    }

    private static void Mark(List<Letter> letters, Accent accent)
    {
        if (letters.Count > 0)
        {
            letters[^1] = letters[^1] with { Accent = accent };
        }
    }

    private enum Accent
    {
        None,
        Acute,
        Circumflex,
        Tilde,
        Cedilla,
    }

    private readonly record struct Letter(char Base, Accent Accent);

    // One sound of the word. A nucleus is a vowel sound that can carry stress (a diphthong is one nucleus).
    private sealed class Unit
    {
        public string Text { get; init; } = string.Empty;

        /// <summary>For a plain vowel, the letter, so that its sound can depend on the stress; otherwise '\0'.</summary>
        public char Vowel { get; init; }

        public Accent Accent { get; init; }

        public bool IsNucleus { get; init; }

        /// <summary>The spelling already says this vowel is stressed (an acute or circumflex accent, or a nasal tilde).</summary>
        public bool Marked { get; init; }

        /// <summary>The vowel closes the word, with at most an "s" after it ("casa", "casas").</summary>
        public bool WordFinal { get; set; }
    }

    private sealed class Parser(List<Letter> letters)
    {
        private readonly List<Unit> _units = [];
        private int _i;

        public List<Unit> Parse()
        {
            while (_i < letters.Count)
            {
                var start = _i;
                Step();

                if (_i == start)
                {
                    _i++;
                }
            }

            MarkWordFinal();
            return _units;
        }

        private char At(int index) => index >= 0 && index < letters.Count ? letters[index].Base : '\0';

        private Accent AccentAt(int index) => index >= 0 && index < letters.Count ? letters[index].Accent : Accent.None;

        private bool IsVowel(int index) => At(index) != '\0' && Vowels.Contains(At(index));

        private bool IsEnd(int index) => index >= letters.Count;

        // The word ends at this letter: nothing follows it, or only a plural "s".
        private bool EndsAfter(int index) => index + 1 >= letters.Count || (index + 2 == letters.Count && At(index + 1) == 's');

        private void Add(string text) => _units.Add(new Unit { Text = text });

        private void AddNucleus(string text, Accent accent = Accent.None, bool marked = false) =>
            _units.Add(new Unit { Text = text, Accent = accent, IsNucleus = true, Marked = marked });

        private void AddVowel(char vowel, Accent accent) =>
            _units.Add(new Unit { Vowel = vowel, Accent = accent, IsNucleus = true, Marked = accent is Accent.Acute or Accent.Circumflex });

        // "e" at the end of the word, unaccented: it sounds like a short "i" and makes the "t" and "d" before it soft.
        private bool IsSoftE(int index) => At(index) == 'e' && AccentAt(index) == Accent.None && EndsAfter(index);

        private bool IsSoftI(int index) => At(index) == 'i' || IsSoftE(index);

        private void Step()
        {
            var c = letters[_i].Base;
            var accent = letters[_i].Accent;

            switch (c)
            {
                case 'a' or 'e' or 'i' or 'o' or 'u':
                    Vowel(c, accent);
                    break;
                case 'b':
                    Add("b");
                    _i++;
                    break;
                case 'c':
                    C(accent);
                    break;
                case 'd':
                    Add(IsSoftI(_i + 1) ? "dʒ" : "d");
                    _i++;
                    break;
                case 'f':
                    Add("f");
                    _i++;
                    break;
                case 'g':
                    G();
                    break;
                case 'h':
                    _i++;
                    break;
                case 'j':
                    Add("ʒ");
                    _i++;
                    break;
                case 'k':
                    Add("k");
                    _i++;
                    break;
                case 'l':
                    L();
                    break;
                case 'm' or 'n':
                    Nasal(c);
                    break;
                case 'p':
                    Add("p");
                    _i++;
                    break;
                case 'q':
                    Q();
                    break;
                case 'r':
                    R();
                    break;
                case 's':
                    S();
                    break;
                case 't':
                    Add(IsSoftI(_i + 1) ? "tʃ" : "t");
                    _i++;
                    break;
                case 'v':
                    Add("v");
                    _i++;
                    break;
                case 'w':
                    Add("w");
                    _i++;
                    break;
                case 'x':
                    X();
                    break;
                case 'y':
                    // "y" starts a word as a consonant ("yanque") and is an "i" elsewhere.
                    if (_i == 0 && IsVowel(_i + 1))
                    {
                        Add("j");
                    }
                    else
                    {
                        AddVowel('i', Accent.None);
                    }

                    _i++;
                    break;
                case 'z':
                    Add(IsEnd(_i + 1) ? "s" : "z");
                    _i++;
                    break;
            }
        }

        private void Vowel(char c, Accent accent)
        {
            var next = At(_i + 1);

            // "ão", "ãe", "õe": nasal diphthongs, always stressed.
            if (accent == Accent.Tilde)
            {
                if (c == 'a' && next == 'o')
                {
                    AddNucleus("ɐ̃ʊ̃", accent, marked: true);
                    _i += 2;
                    return;
                }

                if (next == 'e' && AccentAt(_i + 1) == Accent.None)
                {
                    AddNucleus(c == 'a' ? "ɐ̃j" : "õj", accent, marked: true);
                    _i += 2;
                    return;
                }

                AddNucleus(c == 'a' ? "ɐ̃" : c == 'o' ? "õ" : c + "̃", accent, marked: true);
                _i++;
                return;
            }

            // A vowel followed by "m" or "n" that closes the syllable is nasal ("ponto", "sem").
            if (next is 'm' or 'n' && !IsVowel(_i + 2) && At(_i + 2) != 'h')
            {
                NasalVowel(c, accent);
                return;
            }

            // Falling diphthongs: a, e or o followed by an unaccented i or u.
            if (c is 'a' or 'e' or 'o' && next is 'i' or 'u' && AccentAt(_i + 1) == Accent.None && !(next == 'i' && At(_i + 2) == 'n' && At(_i + 3) == 'h'))
            {
                var open = accent == Accent.Acute;
                var text = (c, next) switch
                {
                    ('a', 'i') => "aɪ",
                    ('a', 'u') => "aʊ",
                    ('e', 'i') => "eɪ",
                    ('e', 'u') => open ? "ɛʊ" : "eʊ",
                    ('o', 'i') => "oɪ",
                    _ => "oʊ",
                };

                AddNucleus(text, accent, marked: accent is Accent.Acute or Accent.Circumflex);
                _i += 2;
                return;
            }

            if (c is 'i' or 'u' && next is 'i' or 'u' && next != c && AccentAt(_i + 1) == Accent.None && accent == Accent.None)
            {
                AddNucleus(c == 'u' ? "uɪ" : "iʊ");
                _i += 2;
                return;
            }

            AddVowel(c, accent);
            _i++;
        }

        // The vowel before a nasal consonant that does not start a syllable, and the consonant itself.
        private void NasalVowel(char c, Accent accent)
        {
            var nasalLetter = At(_i + 1);
            var after = At(_i + 2);
            var consonant = nasalLetter == 'm' && after is 'p' or 'b' ? "m" : "ŋ";
            var text = c switch
            {
                'a' => "ɐ̃",
                'e' => "eɪ",
                'i' => "i",
                'o' => "o",
                _ => "ũ",
            };

            AddNucleus(text, accent, marked: accent is Accent.Acute or Accent.Circumflex);
            Add(consonant);
            _i += 2;
        }

        private void Nasal(char c)
        {
            if (c == 'n' && At(_i + 1) == 'h')
            {
                Add("ɲ");
                _i += 2;
                return;
            }

            Add(c.ToString());
            _i++;
        }

        private void C(Accent accent)
        {
            if (At(_i + 1) == 'h')
            {
                Add("ʃ");
                _i += 2;
                return;
            }

            if (accent == Accent.Cedilla || At(_i + 1) is 'e' or 'i')
            {
                Add("s");
            }
            else
            {
                Add("k");
            }

            _i++;
        }

        private void G()
        {
            if (At(_i + 1) == 'u' && At(_i + 2) is 'e' or 'i' && AccentAt(_i + 1) == Accent.None)
            {
                Add("ɡ");
                _i += 2;
                return;
            }

            if (At(_i + 1) == 'u' && At(_i + 2) is 'a' or 'o')
            {
                Add("ɡw");
                _i += 2;
                return;
            }

            Add(At(_i + 1) is 'e' or 'i' ? "ʒ" : "ɡ");
            _i++;
        }

        private void Q()
        {
            if (At(_i + 1) == 'u')
            {
                Add(At(_i + 2) is 'e' or 'i' ? "k" : "kw");
                _i += 2;
                return;
            }

            Add("k");
            _i++;
        }

        private void L()
        {
            if (At(_i + 1) == 'h')
            {
                Add("lj");
                _i += 2;
                return;
            }

            // "ll" is one "l" (a loan spelling, "Fontenelle").
            if (At(_i + 1) == 'l')
            {
                Add("l");
                _i += 2;
                return;
            }

            // "l" before a consonant or at the end sounds like "u" in Brazil ("Natal", "alfa").
            Add(IsVowel(_i + 1) ? "l" : "ʊ");
            _i++;
        }

        private void R()
        {
            var prev = At(_i - 1);

            if (_i == 0 || prev is 'n' or 'l' or 's')
            {
                Add("x");
                _i++;
            }
            else if (At(_i + 1) == 'r')
            {
                Add("x");
                _i += 2;
            }
            else if (IsVowel(_i + 1))
            {
                // Between vowels a tap; after b, c, d, f, g, p, t, v the "r" is part of the cluster.
                Add(IsVowel(_i - 1) ? "ɾ" : "r");
                _i++;
            }
            else
            {
                // Before a consonant the voice model was trained on a short vowel after the "r"; at the end, a plain "r".
                Add(IsEnd(_i + 1) ? "r" : "ɾə");
                _i++;
            }
        }

        private void S()
        {
            var next = At(_i + 1);

            if (next == 's')
            {
                Add("s");
                _i += 2;
            }
            else if (next == 'c' && At(_i + 2) is 'e' or 'i')
            {
                Add("s");
                _i += 2;
            }
            else
            {
                Add(IsVowel(_i - 1) && IsVowel(_i + 1) ? "z" : "s");
                _i++;
            }
        }

        // "x" has five sounds: "xícara" (ʃ), "exame" (z), "táxi" (ks), "peixe" (ʃ after "ei") and "máximo" (s, not handled).
        private void X()
        {
            if (_i == 0 || (_units.Count > 0 && _units[^1].Text is "eɪ" or "aɪ"))
            {
                Add("ʃ");
            }
            else if (_i == 1 && At(0) == 'e' && IsVowel(_i + 1))
            {
                Add("z");
            }
            else
            {
                Add("ks");
            }

            _i++;
        }

        private void MarkWordFinal()
        {
            var lastNucleus = _units.FindLastIndex(u => u.IsNucleus);

            if (lastNucleus < 0)
            {
                return;
            }

            var after = _units.Skip(lastNucleus + 1).ToList();

            if (after.Count == 0 || (after.Count == 1 && after[0].Text == "s"))
            {
                _units[lastNucleus].WordFinal = true;
            }
        }
    }

    private static string Render(List<Unit> units, string word)
    {
        var nuclei = units.Select((u, i) => (u, i)).Where(t => t.u.IsNucleus).Select(t => t.i).ToList();

        if (nuclei.Count == 0)
        {
            return string.Concat(units.Select(u => u.Text));
        }

        var primary = FindPrimary(units, nuclei, word);
        var secondary = FindSecondary(nuclei.Count, primary);
        var text = new StringBuilder();
        var nucleusIndex = -1;

        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];

            if (!unit.IsNucleus)
            {
                text.Append(unit.Text);
                continue;
            }

            nucleusIndex++;
            var stressed = primary.Contains(nucleusIndex);

            // An unstressed "i" before the final "a" or "o" is a glide: "rádio" is "xˈadʒjʊ", not "xˈadʒiʊ".
            if (IsGlide(units, i, stressed))
            {
                text.Append('j');
                continue;
            }

            text.Append(stressed ? "ˈ" : secondary.Contains(nucleusIndex) ? "ˌ" : string.Empty);
            text.Append(unit.Vowel == ' ' ? unit.Text : RenderVowel(units, i, stressed, nuclei.Count, word));
        }

        return text.ToString();
    }

    private static bool IsGlide(List<Unit> units, int index, bool stressed)
    {
        var unit = units[index];

        return unit.Vowel == 'i'
            && !stressed
            && unit.Accent == Accent.None
            && index > 0
            && !units[index - 1].IsNucleus
            && index + 1 < units.Count
            && units[index + 1] is { Vowel: 'a' or 'o', WordFinal: true };
    }

    // The primary stress: the accent says it; without one, words ending in a, e, o, am, em or ens lean on the one before
    // the last, the rest on the last. Words that lean on the next one (articles, prepositions) have none.
    private static HashSet<int> FindPrimary(List<Unit> units, List<int> nuclei, string word)
    {
        // Every accented vowel is stressed ("púchibéqui" has two).
        var marked = Enumerable.Range(0, nuclei.Count).Where(k => units[nuclei[k]].Marked).ToHashSet();

        if (marked.Count > 0)
        {
            return marked;
        }

        if (Clitics.Contains(word))
        {
            return [];
        }

        if (nuclei.Count == 1)
        {
            return [0];
        }

        var last = units[nuclei[^1]];
        var lastIsPlainEnding = last.WordFinal && (last.Vowel is 'a' or 'e' or 'o' || last.Text is "eɪ" or "ɐ̃");

        // "am", "em": the nasal vowel is followed by "ŋ" (or "m") at the end; "falam", "fazem" lean on the one before.
        var nasalEnding = (last.Text is "ɐ̃" or "eɪ") && IsNasalWordEnd(units, nuclei[^1]);

        return [lastIsPlainEnding || nasalEnding ? nuclei.Count - 2 : nuclei.Count - 1];
    }

    private static bool IsNasalWordEnd(List<Unit> units, int nucleus)
    {
        var tail = units.Skip(nucleus + 1).Select(u => u.Text).ToList();

        return tail is ["ŋ"] or ["m"] or ["ŋ", "s"];
    }

    // Secondary stress, as the voice model heard it: on the first vowel of a word of three or more vowels, and on every
    // second one after it that is not next to the stressed one. Short words of two vowels have none ("natˈaʊ"), except
    // those that carry no primary stress at all ("pˌaɾæ").
    private static HashSet<int> FindSecondary(int count, HashSet<int> primary)
    {
        var set = new HashSet<int>();
        var first = primary.Count == 0 ? -1 : primary.Min();

        if (primary.Count == 0)
        {
            if (count >= 2)
            {
                set.Add(0);
            }

            return set;
        }

        if (count >= 3 && first > 0)
        {
            set.Add(0);
        }

        for (var k = 2; k <= first - 2; k += 2)
        {
            set.Add(k);
        }

        set.ExceptWith(primary);
        return set;
    }

    private static string RenderVowel(List<Unit> units, int index, bool stressed, int nucleusCount, string word)
    {
        var unit = units[index];
        var open = unit.Accent == Accent.Acute || (stressed && !unit.Marked && OpenStress.Contains(word));
        var next = index + 1 < units.Count ? units[index + 1].Text : string.Empty;
        var beforeNasal = next is "m" or "n";

        return unit.Vowel switch
        {
            // Before "m" or "n" that starts a syllable the stressed "a" is nasal ("chame"), the unstressed one is "æ" ("Manaus").
            'a' => stressed ? (beforeNasal ? "ɐ̃" : "a") : beforeNasal || (unit.WordFinal && nucleusCount > 1) ? "æ" : "a",
            'e' => stressed ? (open ? "ɛ" : "e") : unit.WordFinal ? "y" : "e",
            'i' => "i",
            'o' => stressed ? (open ? "ɔ" : "o") : unit.WordFinal ? "ʊ" : "o",
            _ => "u",
        };
    }
}
