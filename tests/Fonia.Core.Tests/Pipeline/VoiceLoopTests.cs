using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;
using Fonia.Core.Speech;
using Fonia.Core.Tests.Audio;
using Fonia.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Core.Tests.Pipeline;

public sealed class VoiceLoopTests : IAsyncDisposable
{
    private const string Own = "PRABC";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AirportContext Solo = AirportContext.Create(TestAirports.WithSolo());

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBXX", null);

    private static readonly SpeechRecorderOptions Quick = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10), TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeCapture _capture = new();
    private readonly FakePushToTalk _ptt = new();
    private readonly FakePlayer _player = new();
    private readonly RecordingObserver _observer = new();
    private readonly FakeSynthesizer _synthesizer;
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
    private Task? _running;
    private VoiceLoop? _loop;
    private string[] _said = [];

    public VoiceLoopTests() => _synthesizer = new FakeSynthesizer(_time);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        if (_running is not null)
        {
            await _running;
        }

        _stop.Dispose();
    }

    private void Start(params string[] pilotSays)
    {
        _said = pilotSays;
        var exchange = new VoiceExchange(
            new FakeTranscriber(_time, call => _said[Math.Min(call, _said.Length - 1)]),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            _synthesizer,
            new VoiceSelector(VoiceCatalog.Default, "pf_dora", new Random(1)),
            RadioIntensity.Off,
            new Random(1),
            _time);
        _loop = new VoiceLoop(new SpeechRecorder(_capture, _ptt, Quick), exchange, _player, Solo, Parked, _observer);

        _running = _loop.RunAsync(_stop.Token);
    }

    // The reply is reported a moment before the channel is free again; a press in that moment is, correctly, ignored as busy.
    private Task WaitForTheChannelAsync() => _observer.WaitForAsync(() => !_loop!.IsBusy, Ct);

    private void Transmit(int milliseconds = 600)
    {
        _ptt.Press();
        _capture.Feed(milliseconds);
        _ptt.Release();
    }

    [Fact]
    public async Task Pressing_speaking_and_releasing_gets_the_answer_in_the_loudspeaker()
    {
        Start("Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento");

        Transmit();
        await _observer.WaitForAsync(() => _player.Played.Count == 1 && _observer.Replies.Count == 1, Ct);

        _observer.Heard.ShouldBe(["Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento"]);
        _observer.Replies.Single().Reply!.Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado, chame pronto para o táxi.");
        _synthesizer.Calls.Single().Voice.Id.ShouldBe("pf_dora");
    }

    [Fact]
    public async Task What_was_heard_is_shown_before_the_answer()
    {
        Start("Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento");

        Transmit();
        await _observer.WaitForAsync(() => _observer.Replies.Count == 1, Ct);

        _observer.Events.ShouldBe(["heard:Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento", "replied"]);
    }

    [Fact]
    public async Task The_flight_goes_on_from_one_exchange_to_the_next()
    {
        Start("Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento", "acionamento aprovado, Papa Romeo Alfa Bravo Charlie");

        Transmit();
        await _observer.WaitForAsync(() => _observer.Replies.Count == 1, Ct);
        await WaitForTheChannelAsync();
        Transmit();
        await _observer.WaitForAsync(() => _observer.Replies.Count == 2, Ct);

        // The second one is the readback of what the first got: nothing to say, and the instruction is no longer pending.
        _observer.Replies[1].Reply.ShouldBeNull();
        _observer.Replies[1].Decision.NextContext.PendingReadback.ShouldBeNull();
        _player.Played.Count.ShouldBe(1);
    }

    [Fact]
    public async Task While_the_controller_speaks_the_key_is_ignored_like_a_busy_channel()
    {
        _player.Hold = true;
        Start("solicito acionamento, Papa Romeo Alfa Bravo Charlie");

        Transmit();
        await _observer.WaitForAsync(() => _player.Played.Count == 1, Ct);
        Transmit();
        await _observer.WaitForAsync(() => _observer.Busies == 1, Ct);

        _synthesizer.Calls.Count.ShouldBe(1, "the second transmission was not even transcribed");

        _player.Hold = false;
        _player.Finish();
        await _observer.WaitForAsync(() => !_loop!.IsBusy, Ct);
        Transmit();
        await _observer.WaitForAsync(() => _observer.Replies.Count == 2, Ct);
    }

    [Fact]
    public async Task A_slip_of_the_finger_is_reported_and_nothing_else_happens()
    {
        Start("x");

        Transmit(milliseconds: 100);
        await _observer.WaitForAsync(() => _observer.TooShorts == 1, Ct);

        _observer.Replies.ShouldBeEmpty();
        _player.Played.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failure_is_reported_and_the_loop_goes_on()
    {
        _synthesizer.Answer = (_, _) => throw new InvalidOperationException("the voice broke");
        Start("solicito acionamento, Papa Romeo Alfa Bravo Charlie", "solicito acionamento, Papa Romeo Alfa Bravo Charlie");

        Transmit();
        await _observer.WaitForAsync(() => _observer.Failures.Count == 1, Ct);
        await WaitForTheChannelAsync();
        _synthesizer.Answer = null;
        Transmit();
        await _observer.WaitForAsync(() => _observer.Replies.Count == 1, Ct);

        _observer.Failures.Single().Message.ShouldBe("the voice broke");
        _player.Played.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_does_not_lose_the_state_of_the_flight()
    {
        _synthesizer.Answer = (_, _) => throw new InvalidOperationException("the voice broke");
        Start("Papa Romeo Alfa Bravo Charlie solicito acionamento", "acionamento aprovado Papa Romeo Alfa Bravo Charlie");

        Transmit();
        await _observer.WaitForAsync(() => _observer.Failures.Count == 1, Ct);
        await WaitForTheChannelAsync();
        _synthesizer.Answer = null;
        Transmit();
        await _observer.WaitForAsync(() => _observer.Replies.Count == 1, Ct);

        // The first never got an answer to the pilot, so the second is not a readback: the controller still has to answer it.
        _observer.Replies.Single().Intent.ShouldBeOfType<Readback>();
    }

    [Fact]
    public async Task The_user_is_told_it_is_listening_only_when_the_microphone_and_the_key_are_open()
    {
        var wasOpen = false;
        var told = new TaskCompletionSource();
        var exchange = new VoiceExchange(
            new FakeTranscriber(_time, _ => "x"), new RuleBasedNlu(), new AtcEngine(), new PortuguesePhraseology(), _synthesizer,
            new VoiceSelector(VoiceCatalog.Default, "pf_dora", new Random(1)), RadioIntensity.Off, new Random(1), _time);
        _loop = new VoiceLoop(new SpeechRecorder(_capture, _ptt, Quick), exchange, _player, Solo, Parked, _observer);

        _running = _loop.RunAsync(_stop.Token, () =>
        {
            wasOpen = _capture.Started && _ptt.Started;
            told.SetResult();
        });
        await told.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        wasOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Stopping_ends_the_loop_and_closes_the_microphone_and_the_key()
    {
        Start("x");
        await _observer.WaitForAsync(() => _capture.Started && _ptt.Started, Ct);

        await _stop.CancelAsync();
        await _running!;

        _capture.Started.ShouldBeFalse();
        _ptt.Started.ShouldBeFalse();
    }

    [Fact]
    public async Task Stopping_in_the_middle_of_the_controllers_speech_cuts_it()
    {
        _player.Hold = true;
        Start("solicito acionamento, Papa Romeo Alfa Bravo Charlie");

        Transmit();
        await _observer.WaitForAsync(() => _player.Played.Count == 1, Ct);
        await _stop.CancelAsync();

        await _running!.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        _observer.Failures.ShouldBeEmpty("stopping is not a failure");
    }
}
