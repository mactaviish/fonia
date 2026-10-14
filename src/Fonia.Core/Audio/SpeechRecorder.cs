namespace Fonia.Core.Audio;

/// <summary>Limits of one transmission.</summary>
/// <param name="MinDuration">Shortest press that counts as speech; a shorter one is a slip of the finger.</param>
/// <param name="MaxDuration">Longest speech; when it is reached the recording ends and the rest of the press is ignored.</param>
/// <param name="PreRoll">
/// Audio kept from just before the press, so the first syllable survives the delay between speaking and the key going
/// down. It does not count for <paramref name="MinDuration"/> or <paramref name="MaxDuration"/>.
/// </param>
public sealed record SpeechRecorderOptions(TimeSpan MinDuration, TimeSpan MaxDuration, TimeSpan PreRoll)
{
    public static SpeechRecorderOptions Default { get; } = new(
        MinDuration: TimeSpan.FromMilliseconds(300),
        MaxDuration: TimeSpan.FromSeconds(30),
        PreRoll: TimeSpan.FromMilliseconds(200));
}

public enum SpeechDiscardReason
{
    /// <summary>The press was shorter than <see cref="SpeechRecorderOptions.MinDuration"/>.</summary>
    TooShort,
}

public sealed class SpeechDiscardedEventArgs(SpeechDiscardReason reason, TimeSpan duration) : EventArgs
{
    public SpeechDiscardReason Reason { get; } = reason;

    /// <summary>How long the key was held (the pre-roll is not counted).</summary>
    public TimeSpan Duration { get; } = duration;
}

/// <summary>
/// Turns the microphone and the push-to-talk into transmissions: while the control is held, the audio is kept;
/// on release it comes out as one <see cref="RecordedSpeech"/>. The microphone stays open the whole time, so
/// pressing the key costs no device start-up and the pre-roll is available.
/// </summary>
/// <remarks>It does not own the capture or the push-to-talk: the caller creates and disposes them.</remarks>
public sealed class SpeechRecorder : IDisposable
{
    private readonly IAudioCapture _capture;
    private readonly IPushToTalk _pushToTalk;
    private readonly AudioFormat _format;
    private readonly int _minBytes;
    private readonly int _maxBytes;
    private readonly int _preRollBytes;
    private readonly Lock _gate = new();
    private readonly Queue<byte[]> _preRoll = new();
    private int _preRollTotal;
    private MemoryStream? _held;
    private byte[] _preRollCopy = [];
    private State _state = State.Idle;
    private bool _disposed;

    private enum State
    {
        Idle,
        Recording,

        /// <summary>The limit was reached and the speech already left: wait for the release.</summary>
        Capped,
    }

    public SpeechRecorder(IAudioCapture capture, IPushToTalk pushToTalk, SpeechRecorderOptions? options = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _pushToTalk = pushToTalk ?? throw new ArgumentNullException(nameof(pushToTalk));
        options ??= SpeechRecorderOptions.Default;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MinDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDuration, options.MinDuration);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PreRoll, TimeSpan.Zero);

        _format = capture.Format;
        _minBytes = _format.BytesFor(options.MinDuration);
        _maxBytes = _format.BytesFor(options.MaxDuration);
        _preRollBytes = _format.BytesFor(options.PreRoll);
    }

    /// <summary>Raised, on the device's thread, when a press ends with speech worth transcribing.</summary>
    public event EventHandler<RecordedSpeech>? SpeechRecorded;

    /// <summary>Raised when a press ends without speech to keep, so the interface can say why nothing happened.</summary>
    public event EventHandler<SpeechDiscardedEventArgs>? SpeechDiscarded;

    /// <summary>Starts the microphone and the push-to-talk.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _capture.DataAvailable += OnData;
        _pushToTalk.Pressed += OnPressed;
        _pushToTalk.Released += OnReleased;
        _pushToTalk.StartWatching();
        _capture.StartCapture();
    }

    /// <summary>Stops listening. An unfinished press is dropped without an event.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capture.DataAvailable -= OnData;
        _pushToTalk.Pressed -= OnPressed;
        _pushToTalk.Released -= OnReleased;
        _capture.StopCapture();
        _pushToTalk.StopWatching();
    }

    private void OnPressed(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_state != State.Idle)
            {
                return;
            }

            _preRollCopy = TakePreRoll();
            _held = new MemoryStream();
            _state = State.Recording;
        }
    }

    private void OnData(object? sender, AudioDataEventArgs e)
    {
        RecordedSpeech? finished = null;

        lock (_gate)
        {
            switch (_state)
            {
                case State.Idle:
                    KeepPreRoll(e.Data.Span);
                    break;

                case State.Recording:
                    var room = _maxBytes - (int)_held!.Length;
                    var take = Math.Min(room, e.Data.Length);
                    _held.Write(e.Data.Span[..take]);

                    if (_held.Length >= _maxBytes)
                    {
                        finished = Finish();
                        _state = State.Capped;
                    }

                    break;
            }
        }

        Raise(finished);
    }

    private void OnReleased(object? sender, EventArgs e)
    {
        RecordedSpeech? finished = null;
        SpeechDiscardedEventArgs? discarded = null;

        lock (_gate)
        {
            switch (_state)
            {
                case State.Recording:
                    var heldBytes = (int)_held!.Length;

                    if (heldBytes >= _minBytes)
                    {
                        finished = Finish();
                    }
                    else
                    {
                        discarded = new SpeechDiscardedEventArgs(SpeechDiscardReason.TooShort, _format.DurationOf(heldBytes));
                        _held = null;
                    }

                    _state = State.Idle;
                    break;

                case State.Capped:
                    _state = State.Idle;
                    break;
            }
        }

        Raise(finished);

        if (discarded is not null)
        {
            SpeechDiscarded?.Invoke(this, discarded);
        }
    }

    private RecordedSpeech Finish()
    {
        var pcm = new byte[_preRollCopy.Length + (int)_held!.Length];
        _preRollCopy.CopyTo(pcm, 0);
        _held.GetBuffer().AsSpan(0, (int)_held.Length).CopyTo(pcm.AsSpan(_preRollCopy.Length));
        _held = null;
        _preRollCopy = [];
        return new RecordedSpeech(_format, pcm);
    }

    private void Raise(RecordedSpeech? speech)
    {
        if (speech is not null)
        {
            SpeechRecorded?.Invoke(this, speech);
        }
    }

    private void KeepPreRoll(ReadOnlySpan<byte> data)
    {
        if (_preRollBytes == 0)
        {
            return;
        }

        _preRoll.Enqueue(data.ToArray());
        _preRollTotal += data.Length;

        // Drop whole blocks while what is left still covers the pre-roll; TakePreRoll trims the exact amount.
        while (_preRoll.Count > 1 && _preRollTotal - _preRoll.Peek().Length >= _preRollBytes)
        {
            _preRollTotal -= _preRoll.Dequeue().Length;
        }
    }

    private byte[] TakePreRoll()
    {
        var all = new byte[_preRollTotal];
        var offset = 0;

        foreach (var block in _preRoll)
        {
            block.CopyTo(all, offset);
            offset += block.Length;
        }

        _preRoll.Clear();
        _preRollTotal = 0;

        var keep = Math.Min(all.Length, _preRollBytes);
        keep -= keep % _format.BytesPerFrame;
        return all[^keep..];
    }
}
