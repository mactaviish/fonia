using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Pipeline;
using Fonia.Core.Sim;
using Fonia.Core.Speech;
using Fonia.Core.Tests.Audio;
using Fonia.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Core.Tests.Pipeline;

public sealed class VoiceLoopFrequencyTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly AirportContext Airport = AirportContext.Create(TestAirports.WithSolo());

    private static readonly FlightContext Parked = new("PRABC", FlightPhase.Parked, "SBXX", null);

    private static readonly SpeechRecorderOptions Quick = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10), TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeCapture _capture = new();
    private readonly FakePushToTalk _ptt = new();
    private readonly FakePlayer _player = new();
    private readonly RecordingObserver _observer = new();
    private readonly FakeSynthesizer _synthesizer;
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
    private VoiceLoop? _loop;
    private Task? _running;
    private Frequency? _com1;

    public VoiceLoopFrequencyTests() => _synthesizer = new FakeSynthesizer(_time);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        if (_running is not null)
        {
            await _running;
        }

        _stop.Dispose();
    }

    private static Frequency Of(AirportFrequencyType type) => Airport.Airport.FrequencyOf(type)!.Frequency;

    private void Start(Func<Frequency?>? com1)
    {
        var exchange = new VoiceExchange(
            new FakeTranscriber(_time, _ => "Papa Romeo Alfa Bravo Charlie solicito acionamento"),
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            _synthesizer,
            new VoiceSelector(VoiceCatalog.Default, null, new Random(1)),
            RadioIntensity.Off,
            new Random(1),
            _time);

        _loop = new VoiceLoop(new SpeechRecorder(_capture, _ptt, Quick), exchange, _player, Airport, Parked, _observer, com1);
        _running = _loop.RunAsync(_stop.Token);
    }

    private async Task TransmitAsync(int expectedReplies)
    {
        _ptt.Press();
        _capture.Feed(600);
        _ptt.Release();
        await _observer.WaitForAsync(() => _observer.Replies.Count == expectedReplies, Ct);
        await _observer.WaitForAsync(() => !_loop!.IsBusy, Ct);
    }

    [Fact]
    public async Task On_the_solos_frequency_the_solo_answers_by_voice()
    {
        _com1 = Of(AirportFrequencyType.Ground);
        Start(() => _com1);

        await TransmitAsync(1);

        _observer.Replies[0].Station.ShouldBe("SOLO Curitiba");
        _observer.Replies[0].Reply!.Text.ShouldContain("acionamento aprovado");
        _synthesizer.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task On_the_torres_frequency_the_torre_answers_with_a_notice_and_no_voice()
    {
        _com1 = Of(AirportFrequencyType.Tower);
        Start(() => _com1);

        await TransmitAsync(1);

        _observer.Replies[0].Station.ShouldBe("TORRE Curitiba");
        _observer.Replies[0].Reply!.Kind.ShouldBe(UtteranceKind.Notice);
        _synthesizer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task On_a_frequency_of_nobody_the_notice_names_the_frequency()
    {
        _com1 = new Frequency(121500);
        Start(() => _com1);

        await TransmitAsync(1);

        _observer.Replies[0].Reply!.Text.ShouldContain("121.5");
        _synthesizer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_frequency_is_read_again_at_every_transmission()
    {
        _com1 = new Frequency(121500);
        Start(() => _com1);

        await TransmitAsync(1);
        _com1 = Of(AirportFrequencyType.Ground);
        await TransmitAsync(2);

        _observer.Replies[1].Station.ShouldBe("SOLO Curitiba");
        _synthesizer.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_radio_reading_the_airports_own_unit_answers()
    {
        Start(null);

        await TransmitAsync(1);

        _observer.Replies[0].Station.ShouldBe("SOLO Curitiba");
    }

    [Fact]
    public async Task When_the_radio_reading_is_not_available_yet_the_airports_own_unit_answers()
    {
        Start(() => null);

        await TransmitAsync(1);

        _observer.Replies[0].Station.ShouldBe("SOLO Curitiba");
    }
}
