using System.Text;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;
using Fonia.Core.Speech;

namespace Fonia.App.Tests;

public sealed class ConsoleVoiceObserverTests : IDisposable
{
    private readonly StringWriter _output = new(new StringBuilder()) { NewLine = "\n" };
    private readonly StringWriter _error = new(new StringBuilder()) { NewLine = "\n" };

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private ConsoleVoiceObserver Create() => new(_output, _error, "SOLO Curitiba");

    private static ExchangeResult Result(Utterance? reply, SynthesizedSpeech? speech, StageTimings? timings = null)
    {
        var flight = new FlightContext("PRABC", FlightPhase.Parked, "SBXX", null);

        return new ExchangeResult(
            "x",
            new Unknown("x"),
            new AtcDecision(new NoResponse(), flight),
            reply,
            speech,
            timings ?? new StageTimings(TimeSpan.FromMilliseconds(2040), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(930), TimeSpan.FromMilliseconds(8)));
    }

    [Fact]
    public void What_was_heard_is_shown_in_quotes()
    {
        Create().OnHeard("solicito acionamento");

        _output.ToString().ShouldBe("Você disse: \"solicito acionamento\"\n");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_heard_is_said_so(string text)
    {
        Create().OnHeard(text);

        _output.ToString().ShouldContain("(nada reconhecido)");
    }

    [Fact]
    public void The_reply_is_shown_with_who_speaks_and_the_time_of_each_stage()
    {
        Create().OnReplied(Result(new Utterance("PAPA ROMEO ALFA BRAVO CHARLIE, prossiga."), new SynthesizedSpeech(new float[24_000], 24_000)));

        var text = _output.ToString();
        text.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, prossiga.");
        text.ShouldContain("tempos: reconhecimento 2,0 s | entendimento 1 ms | voz 930 ms | rádio 8 ms | até o áudio 3,0 s");
    }

    [Fact]
    public void A_message_of_the_app_is_marked_as_such_and_has_no_voice_time()
    {
        Create().OnReplied(Result(
            new Utterance("Sem serviço de controle em SBXX.", UtteranceKind.Notice),
            null,
            new StageTimings(TimeSpan.FromMilliseconds(1500), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)));

        var text = _output.ToString();
        text.ShouldContain("[aviso] Sem serviço de controle em SBXX.");
        text.ShouldNotContain("voz ");
    }

    [Fact]
    public void A_correct_readback_gets_no_answer_and_that_is_said()
    {
        Create().OnReplied(Result(null, null, new StageTimings(TimeSpan.FromMilliseconds(1900), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero)));

        _output.ToString().ShouldContain("(sem resposta do controlador)");
    }

    [Fact]
    public void A_busy_channel_is_explained()
    {
        Create().OnChannelBusy();

        _output.ToString().ShouldContain("canal ocupado");
    }

    [Fact]
    public void A_short_press_is_explained_with_how_long_it_was()
    {
        Create().OnTooShort(TimeSpan.FromMilliseconds(120));

        _output.ToString().ShouldContain("curta demais (120 ms)");
    }

    [Fact]
    public void A_failure_goes_to_the_error_output_with_its_reason()
    {
        Create().OnFailed(new InvalidOperationException("the voice broke"));

        _error.ToString().ShouldContain("fonia: falha na troca de voz: the voice broke");
        _output.ToString().ShouldBeEmpty();
    }
}
