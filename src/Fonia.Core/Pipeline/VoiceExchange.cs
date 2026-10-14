using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Speech;

namespace Fonia.Core.Pipeline;

/// <summary>How long each stage of an exchange took.</summary>
/// <param name="Transcription">Speech to text.</param>
/// <param name="Understanding">The NLU, the ATC engine and the phraseology together: they are rules, not models.</param>
/// <param name="Synthesis">Text to speech.</param>
/// <param name="RadioEffect">The radio effect on the voice.</param>
public sealed record StageTimings(TimeSpan Transcription, TimeSpan Understanding, TimeSpan Synthesis, TimeSpan RadioEffect)
{
    /// <summary>From the pilot letting go of the key to the answer being ready to play: what the pilot waits for.</summary>
    public TimeSpan Total => Transcription + Understanding + Synthesis + RadioEffect;
}

/// <summary>What came out of one transmission of the pilot.</summary>
/// <param name="Heard">What the recognizer wrote down.</param>
/// <param name="Decision">What the ATC engine decided, with the flight afterwards.</param>
/// <param name="Reply">What the controller says, or null when the controller says nothing (a correct readback).</param>
/// <param name="Speech">The controller's voice, or null when there is nothing to say aloud (no reply, or a message of the app).</param>
/// <param name="Station">Who answered, as called on the radio ("SOLO Curitiba"): it depends on the frequency the pilot was on.</param>
public sealed record ExchangeResult(string Heard, Intent Intent, AtcDecision Decision, Utterance? Reply, SynthesizedSpeech? Speech, StageTimings Timings, string Station = "");

/// <summary>
/// One transmission of the pilot through the whole pipeline: speech to text, the NLU, the ATC engine, the phraseology, text
/// to speech and the radio effect. It adds nothing of its own: the decision is the engine's and the words are the templates';
/// this only joins the stages and times them.
/// </summary>
public sealed class VoiceExchange(
    ITranscriber transcriber,
    INlu nlu,
    IAtcEngine engine,
    IPhraseology phraseology,
    ISynthesizer synthesizer,
    VoiceSelector voices,
    RadioIntensity radio,
    Random random,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <param name="speech">What the pilot said, as recorded.</param>
    /// <param name="flight">The state of the flight before this transmission.</param>
    /// <param name="onHeard">Called as soon as the text is known, before the answer is made, so that it can be shown at once.</param>
    public async Task<ExchangeResult> RunAsync(
        RecordedSpeech speech,
        FlightContext flight,
        AirportContext airport,
        Action<string>? onHeard,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(airport);

        var station = phraseology.UnitName(airport);

        var started = _time.GetTimestamp();
        var transcript = await transcriber.TranscribeAsync(speech, new TranscriptionContext(flight.Callsign, station), ct);
        var transcribed = _time.GetElapsedTime(started);
        onHeard?.Invoke(transcript.Text);

        started = _time.GetTimestamp();
        var intent = nlu.Interpret(transcript.Text, flight);
        var decision = engine.Handle(intent, flight, airport);
        var reply = phraseology.Speak(decision.Instruction, new PhraseContext(flight.Callsign, airport));
        var understood = _time.GetElapsedTime(started);

        // Only what the controller says on the radio is voiced; a message of the app is just shown.
        if (reply is not { Kind: UtteranceKind.Radio })
        {
            return new ExchangeResult(transcript.Text, intent, decision, reply, null, new StageTimings(transcribed, understood, TimeSpan.Zero, TimeSpan.Zero), station);
        }

        started = _time.GetTimestamp();
        var voice = await synthesizer.SynthesizeAsync(reply.Text, voices.For(station), ct);
        var synthesized = _time.GetElapsedTime(started);

        started = _time.GetTimestamp();
        var onTheRadio = new SynthesizedSpeech(RadioEffect.Apply(voice.Samples, voice.SampleRate, radio, random), voice.SampleRate);
        var effect = _time.GetElapsedTime(started);

        return new ExchangeResult(transcript.Text, intent, decision, reply, onTheRadio, new StageTimings(transcribed, understood, synthesized, effect), station);
    }
}
