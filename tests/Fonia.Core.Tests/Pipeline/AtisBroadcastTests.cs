using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Pipeline;
using Fonia.Core.Sim;
using Fonia.Core.Speech;
using Fonia.Core.Tests.TestSupport;
using Fonia.Core.Weather;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Core.Tests.Pipeline;

public sealed class AtisBroadcastTests : IAsyncDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Airport WithAtis = TestAirports.Airport(
        "SBXX",
        [TestAirports.Runway("15", "33", 6000)],
        AirportFrequencyType.Ground,
        AirportFrequencyType.Tower,
        AirportFrequencyType.Atis);

    private readonly AirportContext _withWeather = Weather(AirportContext.Create(WithAtis));
    private readonly FakePlayer _player = new();
    private readonly FakeSynthesizer _synthesizer = new(new FakeTimeProvider());
    private readonly List<(string Portuguese, string English)> _told = [];
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
    private Frequency? _tuned;
    private AirportContext _airport;
    private Task? _running;

    public AtisBroadcastTests() => _airport = _withWeather;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();

        if (_running is not null)
        {
            await _running;
        }

        _stop.Dispose();
    }

    private static AirportContext Weather(AirportContext airport)
    {
        MetarParser.TryParse("SBXX 151230Z 15008KT CAVOK 22/14 Q1018", out var metar).ShouldBeTrue();
        return airport.WithWeather(metar, keepRunway: false);
    }

    private static Frequency Of(AirportFrequencyType type) => WithAtis.FrequencyOf(type)!.Frequency;

    private void Start()
    {
        var broadcast = new AtisBroadcast(
            _synthesizer,
            RadioIntensity.Off,
            new Random(1),
            _player,
            () => _airport,
            () => _tuned,
            (portuguese, english) => _told.Add((portuguese, english)),
            pause: TimeSpan.FromMilliseconds(5),
            poll: TimeSpan.FromMilliseconds(5));

        _running = broadcast.RunAsync(_stop.Token);
    }

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);

        while (!condition())
        {
            (DateTime.UtcNow < limit).ShouldBeTrue("the condition never came true");
            await Task.Delay(5, Ct);
        }
    }

    [Fact]
    public async Task On_the_atis_frequency_the_broadcast_plays_again_and_again()
    {
        _tuned = Of(AirportFrequencyType.Atis);
        Start();

        await Until(() => _player.Played.Count >= 3);

        // Voiced once, item by item, slowly; then played from the cache.
        var voiced = _synthesizer.Calls.Count;
        await Task.Delay(50, Ct);
        _synthesizer.Calls.Count.ShouldBe(voiced);
        _synthesizer.Calls.ShouldContain(c => c.Text.Contains("informação MIKE", StringComparison.OrdinalIgnoreCase));
        _synthesizer.Paces.ShouldAllBe(pace => pace < 1.0);
    }

    // The broadcast says everything in Portuguese and then everything in English.
    [Fact]
    public async Task The_broadcast_is_the_portuguese_text_and_then_the_english_one()
    {
        _tuned = Of(AirportFrequencyType.Atis);
        Start();
        await Until(() => _player.Played.Count >= 1);

        var languages = _synthesizer.Calls.Select(c => c.Voice.Language).ToList();
        var firstEnglish = languages.IndexOf("en");

        firstEnglish.ShouldBeGreaterThan(0);
        languages.Take(firstEnglish).ShouldAllBe(l => l == "pt-BR");
        languages.Skip(firstEnglish).ShouldAllBe(l => l == "en");
        _synthesizer.Calls[firstEnglish].Text.ShouldContain("information");
    }

    // What the ATIS says is told once each time the pilot tunes in, in both languages, so that the console can show it.
    [Fact]
    public async Task Tuning_in_tells_the_text_in_both_languages_once_per_tuning()
    {
        _tuned = Of(AirportFrequencyType.Tower);
        Start();
        await Task.Delay(50, Ct);
        _told.ShouldBeEmpty();

        _tuned = Of(AirportFrequencyType.Atis);
        await Until(() => _player.Played.Count >= 3);

        _told.Count.ShouldBe(1);
        _told[0].Portuguese.ShouldContain("informação MIKE");
        _told[0].English.ShouldContain("information mike");

        _tuned = Of(AirportFrequencyType.Tower);
        await Task.Delay(50, Ct);
        _tuned = Of(AirportFrequencyType.Atis);
        await Until(() => _told.Count == 2);
    }

    // The same feminine voice for every airport, Portuguese half and English half alike.
    [Fact]
    public async Task The_voice_of_the_atis_is_always_the_same_feminine_one()
    {
        _tuned = Of(AirportFrequencyType.Atis);
        Start();
        await Until(() => _player.Played.Count >= 1);

        _synthesizer.Calls.Where(c => c.Voice.Language == "pt-BR").ShouldAllBe(c => c.Voice.Id == "pf_dora");
        _synthesizer.Calls.Where(c => c.Voice.Language == "en").ShouldAllBe(c => c.Voice.Id == "af_heart");
    }

    // Nobody is listening and the broadcast is still voiced, ready for the pilot who tunes in.
    [Fact]
    public async Task The_broadcast_is_voiced_ahead_of_anyone_tuning_in()
    {
        _tuned = Of(AirportFrequencyType.Tower);
        Start();

        await Until(() => _synthesizer.Calls.Count > 0);

        _player.Played.ShouldBeEmpty();
    }

    [Fact]
    public async Task On_another_frequency_nothing_plays()
    {
        _tuned = Of(AirportFrequencyType.Tower);
        Start();

        await Task.Delay(100, Ct);

        _player.Played.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_the_weather_there_is_no_broadcast()
    {
        _airport = AirportContext.Create(WithAtis);
        _tuned = Of(AirportFrequencyType.Atis);
        Start();

        await Task.Delay(100, Ct);

        _player.Played.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tuning_away_cuts_the_broadcast_at_once()
    {
        _player.Hold = true;
        _tuned = Of(AirportFrequencyType.Atis);
        Start();
        await Until(() => _player.Played.Count == 1);

        _tuned = Of(AirportFrequencyType.Tower);
        await Task.Delay(100, Ct);

        _player.Played.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Tuning_back_plays_it_again()
    {
        _tuned = Of(AirportFrequencyType.Atis);
        Start();
        await Until(() => _player.Played.Count >= 1);

        _tuned = Of(AirportFrequencyType.Tower);
        await Task.Delay(50, Ct);
        var before = _player.Played.Count;
        _tuned = Of(AirportFrequencyType.Atis);

        await Until(() => _player.Played.Count > before);
    }

    // A player that takes as long as the sound lasts, as a loudspeaker does.
    private sealed class RealTimePlayer : Fonia.Core.Audio.IAudioPlayer
    {
        public List<SynthesizedSpeech> Played { get; } = [];

        public async Task PlayAsync(SynthesizedSpeech speech, CancellationToken ct)
        {
            lock (Played)
            {
                Played.Add(speech);
            }

            await Task.Delay(speech.Duration, ct);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // The broadcast goes on while nobody listens: tuning in finds it in the middle, and tuning away and back finds it further on.
    [Fact]
    public async Task The_loop_goes_on_while_the_radio_is_elsewhere_and_tuning_in_finds_it_in_the_middle()
    {
        var player = new RealTimePlayer();
        var whole = new SynthesizedSpeech(new float[28_800], 24_000);
        _synthesizer.Answer = (_, _) => whole;
        _tuned = Of(AirportFrequencyType.Tower);

        var broadcast = new AtisBroadcast(
            _synthesizer,
            RadioIntensity.Off,
            new Random(1),
            player,
            () => _airport,
            () => _tuned,
            pause: TimeSpan.FromSeconds(10),
            poll: TimeSpan.FromMilliseconds(5));
        _running = broadcast.RunAsync(_stop.Token);

        await Task.Delay(300, Ct);
        _tuned = Of(AirportFrequencyType.Atis);
        await Until(() => player.Played.Count >= 1);

        // The whole broadcast: every item of 1.2 s, 0.6 s between the items of a language and 1.5 s between the languages.
        var items = _synthesizer.Calls.Count;
        var everything = (items * 28_800) + ((items - 2) * 14_400) + 36_000;

        player.Played[0].Samples.Length.ShouldBeInRange(1, everything - 6_000);
    }

    [Fact]
    public void The_text_is_known_without_playing_it()
    {
        AtisBroadcast.TextOn(_withWeather, Of(AirportFrequencyType.Atis)).ShouldNotBeNull();
        AtisBroadcast.TextOn(_withWeather, Of(AirportFrequencyType.Tower)).ShouldBeNull();
        AtisBroadcast.TextOn(_withWeather, null).ShouldBeNull();
    }
}
