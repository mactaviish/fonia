using System.Text;
using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Speech;

namespace Fonia.App.Tests;

public sealed class VoiceModeTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Airport Curitiba = new(
        "SBXX",
        "Curitiba Airport",
        "Curitiba",
        AirportType.Large,
        -25.5,
        -49.1,
        2000,
        "SBXX",
        null,
        "Curitiba",
        SpokenNameSource.Reference,
        true,
        [new Runway(new RunwayEnd("15", 150, null, null), new RunwayEnd("33", 330, null, null), 6000, 150, "ASP", true, false)],
        [new AirportFrequency(AirportFrequencyType.Ground, new Frequency(118000), "Ground")]);

    private static readonly AirportContext Solo = AirportContext.Create(Curitiba);

    private static readonly FlightContext Parked = new("PRABC", FlightPhase.Parked, "SBXX", null);

    private readonly FakeMicrophone _microphone = new();
    private readonly FakeKey _key = new();
    private readonly RecordingPlayer _player = new();
    private readonly WarmableSynthesizer _synthesizer = new();
    private readonly StringWriter _output = new(new StringBuilder()) { NewLine = "\n" };
    private readonly StringWriter _error = new(new StringBuilder()) { NewLine = "\n" };

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private VoiceMode Create(WarmableTranscriber transcriber) =>
        new(
            _microphone,
            _key,
            transcriber,
            _synthesizer,
            _player,
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new VoiceSelector(VoiceCatalog.Default, "pf_dora", new Random(1)),
            RadioIntensity.Off,
            "RControl",
            _output,
            _error,
            new Random(1));

    private async Task WaitForAsync(Func<string> text, string expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!text().Contains(expected, StringComparison.Ordinal))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"'{expected}' did not appear. Output:\n{_output}\nError:\n{_error}");
            }

            await Task.Delay(5, Ct);
        }
    }

    [Fact]
    public async Task It_loads_the_models_listens_and_answers_by_voice()
    {
        var transcriber = new WarmableTranscriber("Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Create(transcriber).RunAsync(Solo, Parked, stop.Token);

        await WaitForAsync(_output.ToString, "Pronto");
        transcriber.WarmedUp.ShouldBeTrue();
        _synthesizer.WarmedUp.ShouldBeTrue();
        _microphone.Started.ShouldBeTrue();

        _key.Press();
        _microphone.Feed(800);
        _key.Release();
        await WaitForAsync(_output.ToString, "tempos:");

        await stop.CancelAsync();
        (await run).ShouldBe(0);

        var text = _output.ToString();
        text.ShouldContain("Carregando os modelos de voz");
        text.ShouldContain("Segure RControl");
        text.ShouldContain("Você disse: \"Solo Curitiba, Papa Romeo Alfa Bravo Charlie, solicito acionamento\"");
        text.ShouldContain("SOLO Curitiba: PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado, chame pronto para o táxi.");
        text.ShouldContain("tempos: reconhecimento ");
        _synthesizer.Said.ShouldContain("PAPA ROMEO ALFA BRAVO CHARLIE, acionamento aprovado, chame pronto para o táxi.");
        _player.Played.Count.ShouldBe(1);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_recognizer_is_warmed_up_with_the_callsign_and_the_station_of_this_flight()
    {
        var transcriber = new WarmableTranscriber("x");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Create(transcriber).RunAsync(Solo, Parked, stop.Token);

        await WaitForAsync(_output.ToString, "Pronto");
        await stop.CancelAsync();
        await run;

        transcriber.Contexts.ShouldHaveSingleItem().ShouldBe(new TranscriptionContext("PRABC", "SOLO Curitiba"));
    }

    [Fact]
    public async Task A_missing_model_ends_before_the_microphone_is_opened_and_says_where_to_get_it()
    {
        var transcriber = new WarmableTranscriber("x", new FileNotFoundException("Modelo de voz não encontrado: ggml-small.bin\nBaixe em https://huggingface.co/"));

        var code = await Create(transcriber).RunAsync(Solo, Parked, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("Modelo de voz não encontrado");
        _error.ToString().ShouldContain("huggingface.co");
        _microphone.Started.ShouldBeFalse();
        _key.Started.ShouldBeFalse();
    }

    [Fact]
    public async Task Another_failure_while_loading_is_reported_not_thrown()
    {
        var transcriber = new WarmableTranscriber("x", new InvalidOperationException("the native library is missing"));

        var code = await Create(transcriber).RunAsync(Solo, Parked, Ct);

        code.ShouldBe(1);
        _error.ToString().ShouldContain("não foi possível carregar os modelos de voz");
        _error.ToString().ShouldContain("the native library is missing");
        _microphone.Started.ShouldBeFalse();
    }

    [Fact]
    public async Task Stopping_closes_the_microphone_and_the_key()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Create(new WarmableTranscriber("x")).RunAsync(Solo, Parked, stop.Token);
        await WaitForAsync(_output.ToString, "Pronto");

        await stop.CancelAsync();
        (await run).ShouldBe(0);

        _microphone.Started.ShouldBeFalse();
        _key.Started.ShouldBeFalse();
    }

    [Fact]
    public async Task A_slip_of_the_finger_is_explained()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Create(new WarmableTranscriber("x")).RunAsync(Solo, Parked, stop.Token);
        await WaitForAsync(_output.ToString, "Pronto");

        _key.Press();
        _microphone.Feed(100);
        _key.Release();
        await WaitForAsync(_output.ToString, "curta demais");

        await stop.CancelAsync();
        await run;
        _player.Played.ShouldBeEmpty();
    }

    [Fact]
    public async Task What_was_not_understood_is_kept_and_the_mark_key_marks_a_mistake_and_is_released_at_the_end()
    {
        var root = Path.Combine(Path.GetTempPath(), "fonia-voice-cases-" + Guid.NewGuid().ToString("N"));
        var mark = new FakeKey();
        CaseLog? log = null;

        try
        {
            var mode = new VoiceMode(
                _microphone,
                _key,
                new WarmableTranscriber("blá blá blá"),
                _synthesizer,
                _player,
                new RuleBasedNlu(),
                new AtcEngine(),
                new PortuguesePhraseology(),
                new VoiceSelector(VoiceCatalog.Default, "pf_dora", new Random(1)),
                RadioIntensity.Off,
                "RControl",
                _output,
                _error,
                new Random(1),
                cases: (airport, callsign) => log = new CaseLog(root, TimeProvider.System, new PortuguesePhraseology(), airport, callsign, saveAudio: true),
                markKey: mark,
                markKeyName: "ScrollLock");
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            var run = mode.RunAsync(Solo, Parked, stop.Token);

            await WaitForAsync(_output.ToString, "Pronto");
            _output.ToString().ShouldContain("aperte ScrollLock");
            mark.Started.ShouldBeTrue();

            _key.Press();
            _microphone.Feed(800);
            _key.Release();
            await WaitForAsync(_output.ToString, "guardado para melhoria: nao-entendido");

            mark.Press();
            await WaitForAsync(_output.ToString, "Troca marcada como errada");

            await stop.CancelAsync();
            (await run).ShouldBe(0);
            mark.Started.ShouldBeFalse();

            log!.Cases.ShouldBe(1);
            Directory.GetFiles(log.SessionDirectory, "*.wav").ShouldHaveSingleItem();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task Without_the_cases_there_is_no_mark_key_and_no_hint_about_it()
    {
        var mark = new FakeKey();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = Create(new WarmableTranscriber("x")).RunAsync(Solo, Parked, stop.Token);

        await WaitForAsync(_output.ToString, "Pronto");
        await stop.CancelAsync();
        await run;

        _output.ToString().ShouldNotContain("marcar");
        mark.Started.ShouldBeFalse();
    }

    [Fact]
    public async Task When_the_simulator_loads_another_flight_the_radio_goes_on_with_the_new_airport_and_aircraft()
    {
        var states = System.Threading.Channels.Channel.CreateUnbounded<AircraftState>();
        var restarts = new List<AircraftState>();
        var guarulhos = Curitiba with { Ident = "SBGR", Name = "Guarulhos Airport", Municipality = "Guarulhos", SpokenName = "Guarulhos", LatitudeDegrees = -23.4356, LongitudeDegrees = -46.4731 };
        var second = AirportContext.Create(guarulhos);
        var mode = new VoiceMode(
            _microphone,
            _key,
            new WarmableTranscriber("Papa Tango Xray Yankee Zulu, solicito acionamento"),
            _synthesizer,
            _player,
            new RuleBasedNlu(),
            new AtcEngine(),
            new PortuguesePhraseology(),
            new VoiceSelector(VoiceCatalog.Default, "pf_dora", new Random(1)),
            RadioIntensity.Off,
            "RControl",
            _output,
            _error,
            new Random(1),
            states: token => states.Reader.ReadAllAsync(token));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = mode.RunAsync(
            Solo,
            Parked,
            stop.Token,
            (state, _) =>
            {
                restarts.Add(state);
                return Task.FromResult<FlightRestart?>(new FlightRestart(second, new FlightContext("PTXYZ", FlightPhase.Parked, "SBGR", null)));
            });

        await WaitForAsync(_output.ToString, "Pronto");
        AircraftState At(double lat, double lon, int seconds, string registration) =>
            new(new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.Zero).AddSeconds(seconds), lat, lon, 3000, 0, 0, 150, true, null, null, 1200, registration, "C172");

        await states.Writer.WriteAsync(At(-25.5, -49.1, 0, "PR-ABC"), Ct);
        await states.Writer.WriteAsync(At(-23.4356, -46.4731, 1, "PT-XYZ"), Ct);

        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (restarts.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5, Ct);
        }

        restarts.ShouldHaveSingleItem().Registration.ShouldBe("PT-XYZ");
        await Task.Delay(100, Ct);

        _key.Press();
        _microphone.Feed(800);
        _key.Release();
        await WaitForAsync(_output.ToString, "SOLO Guarulhos: PAPA TANGO X-RAY YANKEE ZULU, acionamento aprovado");

        await stop.CancelAsync();
        (await run).ShouldBe(0);
    }
}
