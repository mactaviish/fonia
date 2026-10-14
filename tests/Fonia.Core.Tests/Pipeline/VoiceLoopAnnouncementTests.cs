using Fonia.Core.Airports;
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

// The controller speaks first, about what the telemetry showed, once the channel is free.
public sealed class VoiceLoopAnnouncementTests : IAsyncDisposable
{
    private const string Own = "PRABC";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AirportContext Torre =
        AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Tower));

    private static readonly FlightContext Cleared = new(Own, FlightPhase.TakeoffCleared, "SBXX", null, null, null, "11");

    private static readonly TookOff Off = new(new DateTimeOffset(2026, 10, 14, 12, 26, 31, TimeSpan.Zero));

    private static readonly SpeechRecorderOptions Quick = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10), TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeCapture _capture = new();
    private readonly FakePushToTalk _ptt = new();
    private readonly FakePlayer _player = new();
    private readonly RecordingObserver _observer = new();
    private readonly FakeSynthesizer _synthesizer;
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
    private readonly VoiceLoop _loop;
    private readonly Task _running;

    public VoiceLoopAnnouncementTests()
    {
        _synthesizer = new FakeSynthesizer(_time);

        var exchange = new VoiceExchange(
            new FakeTranscriber(_time, _ => "Torre, Papa Romeo Alfa Bravo Charlie, solicito repetir"),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            _synthesizer,
            new VoiceSelector(VoiceCatalog.Default, "pf_dora", new Random(1)),
            RadioIntensity.Off,
            new Random(1),
            _time);
        _loop = new VoiceLoop(new SpeechRecorder(_capture, _ptt, Quick), exchange, _player, Torre, Cleared, _observer);
        _running = _loop.RunAsync(_stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _running;
        _stop.Dispose();
    }

    [Fact]
    public async Task The_torre_says_it_aloud_and_shows_it()
    {
        await _loop.AnnounceAsync(Off, Ct);

        _synthesizer.Calls.Single().Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, decolado aos DOIS MEIA.");
        _player.Played.Count.ShouldBe(1);
        _observer.Announcements.Single().Station.ShouldBe("TORRE Curitiba");
        _observer.Announcements.Single().Decision.NextContext.Phase.ShouldBe(FlightPhase.Airborne);
        _loop.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task The_flight_is_carried_on_so_that_the_same_event_is_said_once()
    {
        await _loop.AnnounceAsync(Off, Ct);
        await _loop.AnnounceAsync(Off, Ct);

        _player.Played.Count.ShouldBe(1);
        _observer.Announcements.Count.ShouldBe(1, "the second one was none of the controller's business and is not even reported");
    }

    [Fact]
    public async Task While_the_channel_is_busy_the_controller_waits_for_it_to_be_free()
    {
        _player.Hold = true;
        _ptt.Press();
        _capture.Feed(600);
        _ptt.Release();
        await _observer.WaitForAsync(() => _observer.Replies.Count == 1 && _loop.IsBusy, Ct);

        var announcing = _loop.AnnounceAsync(Off, Ct);
        await Task.Delay(300, Ct);

        announcing.IsCompleted.ShouldBeFalse();
        _observer.Announcements.ShouldBeEmpty();

        _player.Hold = false;
        _player.Finish();
        await announcing;

        _observer.Announcements.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Stopping_while_waiting_ends_the_wait_without_an_announcement()
    {
        _player.Hold = true;
        _ptt.Press();
        _capture.Feed(600);
        _ptt.Release();
        await _observer.WaitForAsync(() => _loop.IsBusy, Ct);

        using var giveUp = new CancellationTokenSource();
        var announcing = _loop.AnnounceAsync(Off, giveUp.Token);
        await giveUp.CancelAsync();
        await announcing;

        _observer.Announcements.ShouldBeEmpty();
        _player.Hold = false;
        _player.Finish();
    }
}
