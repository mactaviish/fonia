using Fonia.Core.Speech;

namespace Fonia.Core.Tests.Speech;

public class PortuguesePhonemizerTests
{
    // The alphabet of the voice model (Kokoro): any other symbol is dropped before the model hears it.
    private const string ModelAlphabet = ";:,.!?\u2014\u2026\"()\u201C\u201D \u0303\u02A3\u02A5\u02A6\u02A8\u1D5D\uAB67AIOQSTWY\u1D4Aabcdefhijklmnopqrstuvwxyz\u0251\u0250\u0252\u00E6\u03B2\u0254\u0255\u00E7\u0256\u00F0\u02A4\u0259\u025A\u025B\u025C\u025F\u0261\u0265\u0268\u026A\u029D\u026F\u0270\u014B\u0273\u0272\u0274\u00F8\u0278\u03B8\u0153\u0279\u027E\u027B\u0281\u027D\u0282\u0283\u0288\u02A7\u028A\u028B\u028C\u0263\u0264\u03C7\u028E\u0292\u0294\u02C8\u02CC\u02D0\u02B0\u02B2\u2193\u2192\u2197\u2198\u1D7B";

    // Checked against how the voice model was trained to hear these words; a change here is a change in the voice.
    [Theory]
    [InlineData("acionamento", "\u02CCasi\u02CCon\u00E6m\u02C8e\u026A\u014Bt\u028A")]
    [InlineData("aprovado", "\u02CCaprov\u02C8ad\u028A")]
    [InlineData("pronto", "pr\u02C8o\u014Bt\u028A")]
    [InlineData("para", "p\u02CCa\u027E\u00E6")]
    [InlineData("táxi", "t\u02C8aksi")]
    [InlineData("reporte", "x\u02CCep\u02C8\u0254\u027E\u0259t\u0283y")]
    [InlineData("autorizado", "\u02CCa\u028Ato\u027Eiz\u02C8ad\u028A")]
    [InlineData("ponto", "p\u02C8o\u014Bt\u028A")]
    [InlineData("espera", "\u02CCesp\u02C8\u025B\u027E\u00E6")]
    [InlineData("negativo", "n\u02CCe\u0261at\u0283\u02C8iv\u028A")]
    [InlineData("repita", "x\u02CCep\u02C8it\u00E6")]
    [InlineData("prossiga", "pr\u02CCos\u02C8i\u0261\u00E6")]
    [InlineData("estação", "\u02CCestas\u02C8\u0250\u0303\u028A\u0303")]
    [InlineData("chamando", "\u0283\u02CC\u00E6m\u02C8\u0250\u0303\u014Bd\u028A")]
    [InlineData("indicativo", "\u02CCi\u014Bd\u0292ikat\u0283\u02C8iv\u028A")]
    [InlineData("sem", "s\u02C8e\u026A\u014B")]
    [InlineData("serviço", "s\u02CCe\u027E\u0259v\u02C8is\u028A")]
    [InlineData("controle", "k\u02CCo\u014Btr\u02C8\u0254ly")]
    [InlineData("neste", "n\u02C8est\u0283y")]
    [InlineData("aeródromo", "\u02CCae\u027E\u02C8\u0254drom\u028A")]
    [InlineData("apenas", "\u02CCap\u02C8en\u00E6s")]
    [InlineData("informações", "\u02CCi\u014Bfo\u027E\u0259mas\u02C8o\u0303js")]
    [InlineData("voo", "v\u02C8o\u028A")]
    [InlineData("solo", "s\u02C8\u0254l\u028A")]
    [InlineData("torre", "t\u02C8\u0254xy")]
    [InlineData("rádio", "x\u02C8ad\u0292j\u028A")]
    [InlineData("força", "f\u02C8o\u027E\u0259s\u00E6")]
    [InlineData("aérea", "\u02CCa\u02C8\u025B\u027Ee\u00E6")]
    [InlineData("ajuste", "\u02CCa\u0292\u02C8ust\u0283y")]
    [InlineData("altímetro", "\u02CCa\u028At\u0283\u02C8imetr\u028A")]
    [InlineData("pista", "p\u02C8ist\u00E6")]
    [InlineData("esquerda", "\u02CCesk\u02C8e\u027E\u0259d\u00E6")]
    [InlineData("direita", "d\u0292\u02CCi\u027E\u02C8e\u026At\u00E6")]
    [InlineData("centro", "s\u02C8e\u026A\u014Btr\u028A")]
    [InlineData("box", "b\u02C8\u0254ks")]
    [InlineData("bom", "b\u02C8o\u014B")]
    [InlineData("dia", "d\u0292\u02C8i\u00E6")]
    [InlineData("zero", "z\u02C8\u025B\u027E\u028A")]
    [InlineData("informe", "ˌiŋfˈɔɾəmy")]
    [InlineData("hora", "ˈɔɾæ")]
    [InlineData("uno", "\u02C8un\u028A")]
    [InlineData("dois", "d\u02C8o\u026As")]
    [InlineData("três", "tr\u02C8es")]
    [InlineData("quatro", "kw\u02C8atr\u028A")]
    [InlineData("cinco", "s\u02C8i\u014Bk\u028A")]
    [InlineData("meia", "m\u02C8e\u026A\u00E6")]
    [InlineData("sete", "s\u02C8\u025Bt\u0283y")]
    [InlineData("oito", "\u02C8o\u026At\u028A")]
    [InlineData("nove", "n\u02C8\u0254vy")]
    [InlineData("decimal", "d\u02CCesim\u02C8a\u028A")]
    [InlineData("mil", "m\u02C8i\u028A")]
    [InlineData("Curitiba", "k\u02CCu\u027Eit\u0283\u02C8ib\u00E6")]
    [InlineData("Guarulhos", "\u0261w\u02CCa\u027E\u02C8ulj\u028As")]
    [InlineData("Congonhas", "k\u02CCo\u014B\u0261\u02C8o\u0272\u00E6s")]
    [InlineData("Galeão", "\u0261\u02CCale\u02C8\u0250\u0303\u028A\u0303")]
    [InlineData("Brasília", "br\u02CCaz\u02C8ilj\u00E6")]
    [InlineData("Florianópolis", "fl\u02CCo\u027Ei\u00E6n\u02C8\u0254polis")]
    [InlineData("Belém", "bel\u02C8e\u026A\u014B")]
    [InlineData("Manaus", "m\u00E6n\u02C8a\u028As")]
    [InlineData("Natal", "nat\u02C8a\u028A")]
    [InlineData("Salvador", "s\u02CCa\u028Avad\u02C8or")]
    [InlineData("Recife", "x\u02CCes\u02C8ify")]
    [InlineData("Fortaleza", "f\u02CCo\u027E\u0259tal\u02C8ez\u00E6")]
    [InlineData("Porto", "p\u02C8o\u027E\u0259t\u028A")]
    [InlineData("Alegre", "\u02CCal\u02C8\u025B\u0261ry")]
    [InlineData("Londrina", "l\u02CCo\u014Bdr\u02C8in\u00E6")]
    [InlineData("Maringá", "m\u02CCa\u027Ei\u014B\u0261\u02C8a")]
    [InlineData("Viracopos", "v\u02CCi\u027Eak\u02C8\u0254p\u028As")]
    [InlineData("Goiânia", "\u0261\u02CCo\u026A\u02C8\u0250\u0303nj\u00E6")]
    [InlineData("Cuiabá", "k\u02CCu\u026Aab\u02C8a")]
    [InlineData("Foz", "f\u02C8\u0254s")]
    [InlineData("Iguaçu", "\u02CCi\u0261was\u02C8u")]
    [InlineData("Santos", "s\u02C8\u0250\u0303\u014Bt\u028As")]
    [InlineData("Campo", "k\u02C8\u0250\u0303mp\u028A")]
    [InlineData("Grande", "\u0261r\u02C8\u0250\u0303\u014Bd\u0292y")]
    [InlineData("Presidente", "pr\u02CCezid\u02C8e\u026A\u014Bt\u0283y")]
    [InlineData("Prudente", "pr\u02CCud\u02C8e\u026A\u014Bt\u0283y")]
    [InlineData("São", "s\u02C8\u0250\u0303\u028A\u0303")]
    [InlineData("José", "\u0292oz\u02C8\u025B")]
    [InlineData("dos", "d\u028As")]
    [InlineData("Campos", "k\u02C8\u0250\u0303mp\u028As")]
    [InlineData("Ribeirão", "x\u02CCibe\u026A\u027E\u02C8\u0250\u0303\u028A\u0303")]
    [InlineData("Preto", "pr\u02C8et\u028A")]
    [InlineData("Uberlândia", "\u02CCube\u027E\u0259l\u02C8\u0250\u0303\u014Bd\u0292j\u00E6")]
    [InlineData("Navegantes", "n\u02CCave\u0261\u02C8\u0250\u0303\u014Bt\u0283ys")]
    public void Words_are_read_the_way_the_voice_model_expects(string word, string expected)
    {
        PortuguesePhonemizer.PhonemizeWord(word).ShouldBe(expected);
    }

    [Fact]
    public void Every_word_of_the_phraseology_and_the_airports_uses_only_the_alphabet_of_the_model()
    {
        string[] words = ["acionamento", "aprovado", "pronto", "para", "táxi", "reporte", "autorizado", "ponto", "espera", "negativo", "repita", "prossiga", "estação", "chamando", "indicativo", "sem", "serviço", "controle", "neste", "aeródromo", "apenas", "informações", "voo", "solo", "torre", "rádio", "força", "aérea", "ajuste", "altímetro", "pista", "esquerda", "direita", "centro", "box", "bom", "dia", "zero", "uno", "dois", "três", "quatro", "cinco", "meia", "sete", "oito", "nove", "decimal", "mil", "Curitiba", "Guarulhos", "Congonhas", "Galeão", "Brasília", "Florianópolis", "Belém", "Manaus", "Natal", "Salvador", "Recife", "Fortaleza", "Porto", "Alegre", "Londrina", "Maringá", "Viracopos", "Goiânia", "Cuiabá", "Foz", "Iguaçu", "Santos", "Campo", "Grande", "Presidente", "Prudente", "São", "José", "dos", "Campos", "Ribeirão", "Preto", "Uberlândia", "Navegantes"];

        foreach (var word in words)
        {
            PortuguesePhonemizer.PhonemizeWord(word).Where(c => !ModelAlphabet.Contains(c)).ShouldBeEmpty(word);
        }
    }
}
