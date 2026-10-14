using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;
using Fonia.Core.Speech;
using Fonia.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Core.Tests.Pipeline;

public class VoiceExchangeTests
{
    private const string Own = "PRABC";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AirportContext Solo = AirportContext.Create(TestAirports.WithSolo());

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBXX", null);

    private static readonly RecordedSpeech Speech = new(AudioFormat.Speech, new byte[AudioFormat.Speech.BytesFor(TimeSpan.FromSeconds(2))]);

    private readonly FakeTimeProvider _time = new();

    private VoiceExchange Create(
        FakeTranscriber transcriber,
        FakeSynthesizer synthesizer,
        RadioIntensity radio = RadioIntensity.Off,
        string? fixedVoice = null) =>
        new(
            transcriber,
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            synthesizer,
            new VoiceSelector(VoiceCatalog.Default, fixedVoice, new Random(1)),
            radio,
            new Random(1),
            _time);

    [Fact]
    public async Task A_request_goes_through_every_stage_and_comes_out_as_the_controllers_voice()
    {
        var synthesizer = new FakeSynthesizer(_time);
        var exchange = Create(new FakeTranscriber(_time, _ => "Solo Curitiba, PRABC, solicito acionamento"), synthesizer);

        var result = await exchange.RunAsync(Speech, Parked, Solo, null, Ct);

        result.Heard.ShouldBe("Solo Curitiba, PRABC, solicito acionamento");
        result.Intent.ShouldBe(new RequestStartup(Own, null));
        result.Decision.Instruction.ShouldBe(new StartupApproved());
        result.Decision.NextContext.PendingReadback.ShouldBe(new StartupApproved());
        result.Reply.ShouldNotBeNull().Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado, chame pronto para o táxi.");
        result.Speech.ShouldNotBeNull().SampleRate.ShouldBe(24_000);
        synthesizer.Calls.ShouldHaveSingleItem().Text.ShouldBe(result.Reply.Text);
    }

    [Fact]
    public async Task The_recognizer_is_told_the_callsign_and_who_answers()
    {
        var transcriber = new FakeTranscriber(_time, _ => "solicito acionamento");
        var exchange = Create(transcriber, new FakeSynthesizer(_time));

        await exchange.RunAsync(Speech, Parked, Solo, null, Ct);

        transcriber.Contexts.ShouldHaveSingleItem().ShouldBe(new TranscriptionContext(Own, "SOLO Curitiba"));
    }

    [Fact]
    public async Task What_was_heard_is_reported_before_the_voice_is_made_so_that_it_can_be_shown_at_once()
    {
        var order = new List<string>();
        var synthesizer = new FakeSynthesizer(_time) { Answer = (_, _) => { order.Add("voice"); return FakeSynthesizer.OneSecondOfTone(); } };
        var exchange = Create(new FakeTranscriber(_time, _ => "solicito acionamento"), synthesizer);

        await exchange.RunAsync(Speech, Parked, Solo, heard => order.Add("heard:" + heard), Ct);

        order.ShouldBe(["heard:solicito acionamento", "voice"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hmm blá blá")]
    public async Task What_makes_no_sense_gets_a_request_to_say_it_again_and_changes_nothing(string heard)
    {
        var synthesizer = new FakeSynthesizer(_time);
        var exchange = Create(new FakeTranscriber(_time, _ => heard), synthesizer);

        var result = await exchange.RunAsync(Speech, Parked, Solo, null, Ct);

        result.Reply.ShouldNotBeNull().Text.ShouldBe("Repita.");
        result.Decision.NextContext.ShouldBe(Parked);
        synthesizer.Calls.ShouldHaveSingleItem().Text.ShouldBe("Repita.");
    }

    [Fact]
    public async Task A_correct_readback_gets_no_answer_so_nothing_is_said()
    {
        var synthesizer = new FakeSynthesizer(_time);
        var exchange = Create(new FakeTranscriber(_time, _ => "acionamento aprovado, PRABC"), synthesizer);
        var waiting = Parked with { PendingReadback = new StartupApproved(), Phase = FlightPhase.StartupApproved };

        var result = await exchange.RunAsync(Speech, waiting, Solo, null, Ct);

        result.Reply.ShouldBeNull();
        result.Speech.ShouldBeNull();
        synthesizer.Calls.ShouldBeEmpty();
        result.Decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public async Task A_message_of_the_app_is_shown_but_never_spoken()
    {
        var noService = AirportContext.Create(TestAirports.Airport("SBZZ", [TestAirports.Runway("15", "33", 6000)]));
        var synthesizer = new FakeSynthesizer(_time);
        var exchange = Create(new FakeTranscriber(_time, _ => "solicito acionamento PRABC"), synthesizer);

        var result = await exchange.RunAsync(Speech, Parked, noService, null, Ct);

        result.Reply.ShouldNotBeNull().Kind.ShouldBe(UtteranceKind.Notice);
        result.Speech.ShouldBeNull();
        synthesizer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_stage_is_timed_and_the_total_is_what_the_pilot_waits()
    {
        var exchange = Create(
            new FakeTranscriber(_time, _ => "solicito acionamento", takes: TimeSpan.FromSeconds(2)),
            new FakeSynthesizer(_time, takes: TimeSpan.FromMilliseconds(900)));

        var result = await exchange.RunAsync(Speech, Parked, Solo, null, Ct);

        result.Timings.Transcription.ShouldBe(TimeSpan.FromSeconds(2));
        result.Timings.Synthesis.ShouldBe(TimeSpan.FromMilliseconds(900));
        result.Timings.Understanding.ShouldBe(TimeSpan.Zero);
        result.Timings.RadioEffect.ShouldBe(TimeSpan.Zero);
        result.Timings.Total.ShouldBe(TimeSpan.FromMilliseconds(2900));
    }

    [Fact]
    public async Task The_radio_effect_is_applied_to_the_voice_and_can_be_turned_off()
    {
        var off = await Create(new FakeTranscriber(_time, _ => "solicito acionamento"), new FakeSynthesizer(_time), RadioIntensity.Off)
            .RunAsync(Speech, Parked, Solo, null, Ct);
        var medium = await Create(new FakeTranscriber(_time, _ => "solicito acionamento"), new FakeSynthesizer(_time), RadioIntensity.Medium)
            .RunAsync(Speech, Parked, Solo, null, Ct);

        off.Speech!.Samples.Length.ShouldBe(24_000);
        medium.Speech!.Samples.Length.ShouldBeGreaterThan(24_000, "the click before and the squelch after");
    }

    [Fact]
    public async Task A_station_keeps_its_voice_from_one_exchange_to_the_next()
    {
        var synthesizer = new FakeSynthesizer(_time);
        var exchange = Create(new FakeTranscriber(_time, _ => "blá"), synthesizer);

        for (var i = 0; i < 6; i++)
        {
            await exchange.RunAsync(Speech, Parked, Solo, null, Ct);
        }

        synthesizer.Calls.Select(c => c.Voice).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_voice_fixed_by_the_user_is_the_one_used()
    {
        var synthesizer = new FakeSynthesizer(_time);
        var exchange = Create(new FakeTranscriber(_time, _ => "blá"), synthesizer, fixedVoice: "pm_alex");

        await exchange.RunAsync(Speech, Parked, Solo, null, Ct);

        synthesizer.Calls.Single().Voice.Id.ShouldBe("pm_alex");
    }

    [Fact]
    public async Task A_cancelled_exchange_stops()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var exchange = Create(new FakeTranscriber(_time, _ => "x"), new FakeSynthesizer(_time));

        await Should.ThrowAsync<OperationCanceledException>(() => exchange.RunAsync(Speech, Parked, Solo, null, cancellation.Token));
    }
}
