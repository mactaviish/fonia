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

// The simulator loads another flight while the radio is on: the loop goes on with the new airport and the new aircraft.
public sealed class VoiceLoopNewFlightTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AirportContext First = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext Second = AirportContext.Create(
        TestAirports.Airport("SBGR", [TestAirports.Runway("09", "27", 9000)], AirportFrequencyType.Ground, AirportFrequencyType.Tower) with { SpokenName = "Guarulhos" });

    private static readonly FlightContext Taxiing = new("PRABC", FlightPhase.Taxiing, "SBXX", new TaxiClearance("11"));

    private static readonly FlightContext NewFlight = new("PTXYZ", FlightPhase.Parked, "SBGR", null);

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
    private string _said = "Papa Romeo Alfa Bravo Charlie, solicito acionamento";

    public VoiceLoopNewFlightTests()
    {
        _synthesizer = new FakeSynthesizer(_time);

        var exchange = new VoiceExchange(
            new FakeTranscriber(_time, _ => _said),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            _synthesizer,
            new VoiceSelector(VoiceCatalog.Default, "pf_dora", new Random(1)),
            RadioIntensity.Off,
            new Random(1),
            _time);
        _loop = new VoiceLoop(new SpeechRecorder(_capture, _ptt, Quick), exchange, _player, First, Taxiing, _observer);
        _running = _loop.RunAsync(_stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _running;
        _stop.Dispose();
    }

    private async Task SpeakAsync()
    {
        var before = _observer.Replies.Count;
        _ptt.Press();
        _capture.Feed(600);
        _ptt.Release();
        await _observer.WaitForAsync(() => _observer.Replies.Count > before && !_loop.IsBusy, Ct);
    }

    [Fact]
    public async Task After_a_new_flight_the_controller_is_the_one_of_the_new_airport_and_the_flight_starts_over()
    {
        _loop.StartFlight(Second, NewFlight);
        _said = "Papa Tango Xray Yankee Zulu, solicito acionamento";

        await SpeakAsync();

        var reply = _observer.Replies.Single();
        reply.Station.ShouldBe("SOLO Guarulhos");
        reply.Reply!.Text.ShouldBe("PAPA TANGO X-RAY YANKEE ZULU, acionamento aprovado, chame pronto para o táxi.");
        reply.Decision.NextContext.AirportIcao.ShouldBe("SBGR");
    }

    [Fact]
    public async Task What_waited_for_a_readback_in_the_old_flight_does_not_follow_to_the_new_one()
    {
        _loop.StartFlight(Second, NewFlight);
        _said = "Papa Tango Xray Yankee Zulu, pista uno uno";

        await SpeakAsync();

        // In the old flight this was the readback of the taxi clearance. In the new one nothing is waiting: it is not a readback.
        _observer.Replies.Single().Intent.ShouldNotBeOfType<Readback>();
    }

    [Fact]
    public async Task Without_a_new_flight_the_loop_goes_on_as_before()
    {
        _said = "Papa Romeo Alfa Bravo Charlie, pista uno uno";

        await SpeakAsync();

        _observer.Replies.Single().Station.ShouldBe("SOLO Curitiba");
        _observer.Replies.Single().Decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public async Task An_exchange_that_was_going_on_when_the_flight_changed_does_not_bring_the_old_flight_back()
    {
        _player.Hold = true;
        _ptt.Press();
        _capture.Feed(600);
        _ptt.Release();
        await _observer.WaitForAsync(() => _player.Played.Count == 1, Ct);

        _loop.StartFlight(Second, NewFlight);
        _player.Hold = false;
        _player.Finish();
        await _observer.WaitForAsync(() => !_loop.IsBusy, Ct);
        _said = "Papa Tango Xray Yankee Zulu, solicito acionamento";
        await SpeakAsync();

        // The second exchange runs on the new flight, not on what the first one left behind.
        _observer.Replies[^1].Station.ShouldBe("SOLO Guarulhos");
        _observer.Replies[^1].Decision.NextContext.AirportIcao.ShouldBe("SBGR");
    }
}
