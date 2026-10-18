using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Audio;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Speech;

namespace Fonia.Core.Pipeline;

/// <summary>
/// The ATIS of the airport on the air (ICA 100-37, Art. 759: a continuous, repeated broadcast). Like the real one it never stops: from the
/// moment the app has the weather of the airport the broadcast runs on its own clock, text, pause, text, pause, whether anyone listens or not.
/// The broadcast is the text in Portuguese and then in English (ICA 100-37, Art. 761 §2º), and then it starts again. It is voiced ahead, as soon as
/// the text exists, so that tuning in finds it ready. Tuning the radio to the ATIS frequency only lets the pilot hear it, from wherever the loop is then; tuning away cuts the sound at once.
/// Nobody answers a call on this frequency, so it never overlaps a reply of the controller.
/// </summary>
/// <param name="airport">The airport the radio is at now, with its weather (it may change when another flight is loaded).</param>
/// <param name="com1">The frequency the radio is on; null when it is not known, and then nothing is played.</param>
/// <param name="onTunedIn">Called with the text in Portuguese and in English each time the pilot tunes in (or the text changes while tuned).</param>
public sealed class AtisBroadcast(
    ISynthesizer synthesizer,
    RadioIntensity radio,
    Random random,
    IAudioPlayer player,
    Func<AirportContext> airport,
    Func<Frequency?> com1,
    Action<string, string>? onTunedIn = null,
    Action<Exception>? onFailed = null,
    TimeSpan? pause = null,
    TimeSpan? poll = null,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan DefaultPause = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultPoll = TimeSpan.FromMilliseconds(250);
    // Read slowly, as a recording meant to be copied is.
    private const double Pace = 0.85;


    // The silence between the items of the ATIS, and between the two languages: each item is voiced by itself, as the recording of a person is read.
    private static readonly TimeSpan BetweenItems = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan BetweenLanguages = TimeSpan.FromMilliseconds(1500);

    // What is left of the text is not worth starting to play.
    private static readonly TimeSpan ShortestTail = TimeSpan.FromMilliseconds(300);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _pause = pause ?? DefaultPause;
    private readonly TimeSpan _poll = poll ?? DefaultPoll;

    // Its own source of randomness: the broadcast is made while the pilot may be talking, and a Random is not for two threads.
    private readonly Random _random = new(random.Next());

    // The voice of the last text, so a broadcast that repeats is not made again.
    // The text whose voicing failed: it is not tried again until the text changes.
    private string? _failed;
    private string? _cachedText;
    private SynthesizedSpeech? _cachedSpeech;

    // The broadcast of the moment: its text and when its loop began. A new text (a new METAR) begins the loop again.
    private string? _onTheAir;
    private long _began;

    /// <summary>The text the airport broadcasts, or null when the app has no weather for it or the airport has no ATIS frequency.</summary>
    public static string? BroadcastOf(AirportContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Report is { } report && context.Airport.FrequencyOf(AirportFrequencyType.Atis) is not null
            ? AtisText.Combined(context, report)
            : null;
    }

    /// <summary>The text on the air for this airport and this frequency, or null when the radio is not on its ATIS or there is no broadcast.</summary>
    public static string? TextOn(AirportContext context, Frequency? tuned)
    {
        ArgumentNullException.ThrowIfNull(context);

        return tuned is { } frequency && FrequencyResolver.IsAtis(context.Airport, frequency) ? BroadcastOf(context) : null;
    }

    /// <summary>Runs until <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        string? told = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var context = airport();
                var broadcast = BroadcastOf(context);
                Keep(broadcast);

                if (broadcast is not null && broadcast != _failed)
                {
                    await PrepareAsync(context, broadcast, ct);
                }

                var text = TextOn(context, com1());

                if (text is null || text == _failed)
                {
                    told = null;
                    await Task.Delay(_poll, _time, ct);
                    continue;
                }

                if (text != told)
                {
                    told = text;
                    onTunedIn?.Invoke(text, string.Join(". ", AtisEnglish.Sentences(context, context.Report!)) + ".");
                }

                await ListenAsync(context, text, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The radio is stopping.
        }
    }

    // Voices the broadcast ahead, so that the pilot who tunes in hears it at once.
    private async Task PrepareAsync(AirportContext context, string broadcast, CancellationToken ct)
    {
        try
        {
            await VoiceAsync(context, broadcast, ct);
        }
#pragma warning disable CA1031 // A broadcast can fail in many ways (models, devices); it must not end the radio.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _failed = broadcast;
            onFailed?.Invoke(ex);
        }
    }

    // The loop goes on while nobody listens: it begins when the text first exists and again when the text changes.
    private void Keep(string? broadcast)
    {
        if (broadcast != _onTheAir)
        {
            _onTheAir = broadcast;
            _began = _time.GetTimestamp();
        }
    }

    // The pilot is tuned in: plays what the loop is saying at this moment, or waits for the pause to pass.
    private async Task ListenAsync(AirportContext context, string text, CancellationToken ct)
    {
        using var heard = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watching = WatchAsync(text, heard);

        try
        {
            var speech = await VoiceAsync(context, text, heard.Token);
            var cycle = speech.Duration + _pause;
            var into = TimeSpan.FromTicks(_time.GetElapsedTime(_began).Ticks % cycle.Ticks);

            if (speech.Duration - into > ShortestTail)
            {
                var skipped = (int)(into.TotalSeconds * speech.SampleRate);
                await player.PlayAsync(new SynthesizedSpeech(speech.Samples[skipped..], speech.SampleRate), heard.Token);
            }
            else
            {
                await Task.Delay(cycle - into, _time, heard.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The frequency or the weather changed: what was heard is cut, and the loop looks again.
        }
#pragma warning disable CA1031 // A broadcast can fail in many ways (models, devices); it must not end the radio.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _failed = text;
            onFailed?.Invoke(ex);
        }
        finally
        {
            await heard.CancelAsync();
            await watching;
        }
    }

    // Ends the listening (cancelling its token) when the radio leaves the frequency or the text is no longer the one on the air.
    private async Task WatchAsync(string text, CancellationTokenSource heard)
    {
        try
        {
            while (!heard.IsCancellationRequested)
            {
                await Task.Delay(_poll, _time, heard.Token);

                if (TextOn(airport(), com1()) != text)
                {
                    await heard.CancelAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The listening ended by itself.
        }
    }

    private async Task<SynthesizedSpeech> VoiceAsync(AirportContext context, string text, CancellationToken ct)
    {
        if (_cachedSpeech is not null && _cachedText == text)
        {
            return _cachedSpeech;
        }

        var report = context.Report!;
        var samples = new List<float>();
        var rate = 0;

        rate = await AppendAsync(samples, rate, AtisText.Sentences(context, report), VoiceCatalog.AtisPortuguese, ct);
        samples.AddRange(new float[(int)(BetweenLanguages.TotalSeconds * rate)]);
        rate = await AppendAsync(samples, rate, AtisEnglish.Sentences(context, report), VoiceCatalog.AtisEnglish, ct);

        var onTheRadio = new SynthesizedSpeech(RadioEffect.Apply([.. samples], rate, radio, _random), rate);

        _cachedText = text;
        _cachedSpeech = onTheRadio;
        return onTheRadio;
    }

    // An item of a single word ("CAVOK") is too short for the model to say well on its own: it is said in the breath of the one before it.
    private static List<string> WithoutShortItems(IReadOnlyList<string> items)
    {
        var joined = new List<string>();

        foreach (var item in items)
        {
            if (joined.Count > 0 && !item.Contains(' ', StringComparison.Ordinal))
            {
                joined[^1] += ". " + item;
            }
            else
            {
                joined.Add(item);
            }
        }

        return [.. joined.Select(item => item + ".")];
    }

    // Voices the items one by one, slowly, with a silence after each but the last.
    private async Task<int> AppendAsync(List<float> samples, int rate, IReadOnlyList<string> items, Voice voice, CancellationToken ct)
    {
        var spokenItems = WithoutShortItems(items);

        for (var i = 0; i < spokenItems.Count; i++)
        {
            var spoken = await synthesizer.SynthesizeAsync(spokenItems[i], voice, ct, Pace);
            rate = spoken.SampleRate;
            samples.AddRange(spoken.Samples);

            if (i < spokenItems.Count - 1)
            {
                samples.AddRange(new float[(int)(BetweenItems.TotalSeconds * rate)]);
            }
        }

        return rate;
    }
}
