using Fonia.Core.Audio;

namespace Fonia.Core.Tests.Audio;

public sealed class SpeechRecorderTests : IDisposable
{
    private static readonly SpeechRecorderOptions NoPreRoll = new(
        MinDuration: TimeSpan.FromMilliseconds(300),
        MaxDuration: TimeSpan.FromSeconds(2),
        PreRoll: TimeSpan.Zero);

    private readonly FakeCapture _capture = new();
    private readonly FakePushToTalk _ptt = new();
    private readonly List<RecordedSpeech> _recorded = [];
    private readonly List<SpeechDiscardedEventArgs> _discarded = [];

    public void Dispose()
    {
        _capture.Dispose();
        _ptt.Dispose();
    }

    private SpeechRecorder Create(SpeechRecorderOptions? options = null)
    {
        var recorder = new SpeechRecorder(_capture, _ptt, options ?? NoPreRoll);
        recorder.SpeechRecorded += (_, speech) => _recorded.Add(speech);
        recorder.SpeechDiscarded += (_, discarded) => _discarded.Add(discarded);
        return recorder;
    }

    [Fact]
    public void Start_starts_the_capture_and_the_push_to_talk()
    {
        using var recorder = Create();

        recorder.Start();

        _capture.Started.ShouldBeTrue();
        _ptt.Started.ShouldBeTrue();
    }

    [Fact]
    public void It_records_what_is_said_between_pressing_and_releasing()
    {
        using var recorder = Create();
        recorder.Start();

        _capture.Feed(500, value: 9); // before the press: not recorded
        _ptt.Press();
        _capture.Feed(400, value: 1);
        _capture.Feed(400, value: 2);
        _ptt.Release();
        _capture.Feed(500, value: 9); // after the release: not recorded

        var speech = _recorded.ShouldHaveSingleItem();
        speech.Format.ShouldBe(AudioFormat.Speech);
        speech.Duration.ShouldBe(TimeSpan.FromMilliseconds(800));
        speech.Pcm.Take(10).ShouldAllBe(b => b == 1);
        speech.Pcm.TakeLast(10).ShouldAllBe(b => b == 2);
        _discarded.ShouldBeEmpty();
    }

    [Fact]
    public void It_discards_a_press_shorter_than_the_minimum()
    {
        using var recorder = Create();
        recorder.Start();

        _ptt.Press();
        _capture.Feed(100);
        _ptt.Release();

        _recorded.ShouldBeEmpty();
        var discarded = _discarded.ShouldHaveSingleItem();
        discarded.Reason.ShouldBe(SpeechDiscardReason.TooShort);
        discarded.Duration.ShouldBe(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void It_keeps_a_press_exactly_as_long_as_the_minimum()
    {
        using var recorder = Create();
        recorder.Start();

        _ptt.Press();
        _capture.Feed(300);
        _ptt.Release();

        _recorded.ShouldHaveSingleItem();
        _discarded.ShouldBeEmpty();
    }

    [Fact]
    public void A_press_with_no_audio_is_discarded_as_too_short()
    {
        using var recorder = Create();
        recorder.Start();

        _ptt.Press();
        _ptt.Release();

        _recorded.ShouldBeEmpty();
        _discarded.ShouldHaveSingleItem().Duration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void It_ends_the_speech_at_the_maximum_and_ignores_the_rest_of_the_press()
    {
        using var recorder = Create();
        recorder.Start();

        _ptt.Press();
        _capture.Feed(1500);
        _capture.Feed(1500); // crosses the 2 s limit
        _capture.Feed(1000);

        var speech = _recorded.ShouldHaveSingleItem();
        speech.Duration.ShouldBe(TimeSpan.FromSeconds(2));

        _ptt.Release();
        _recorded.Count.ShouldBe(1, "releasing after the limit must not record a second speech");
        _discarded.ShouldBeEmpty();
    }

    [Fact]
    public void A_new_press_after_the_limit_records_again()
    {
        using var recorder = Create();
        recorder.Start();

        _ptt.Press();
        _capture.Feed(2500);
        _ptt.Release();
        _ptt.Press();
        _capture.Feed(500);
        _ptt.Release();

        _recorded.Count.ShouldBe(2);
        _recorded[1].Duration.ShouldBe(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Pre_roll_adds_the_audio_just_before_the_press_without_counting_for_the_minimum()
    {
        var options = NoPreRoll with { PreRoll = TimeSpan.FromMilliseconds(200) };
        using var recorder = Create(options);
        recorder.Start();

        _capture.Feed(1000, value: 7);
        _capture.Feed(100, value: 8);
        _capture.Feed(100, value: 8);
        _ptt.Press();
        _capture.Feed(300, value: 1);
        _ptt.Release();

        var speech = _recorded.ShouldHaveSingleItem();
        speech.Duration.ShouldBe(TimeSpan.FromMilliseconds(500), "200 ms of pre-roll plus 300 ms held");
        speech.Pcm.Take(10).ShouldAllBe(b => b == 8);
        speech.Pcm.TakeLast(10).ShouldAllBe(b => b == 1);
    }

    [Fact]
    public void Pre_roll_does_not_make_a_short_tap_pass_the_minimum()
    {
        var options = NoPreRoll with { PreRoll = TimeSpan.FromMilliseconds(400) };
        using var recorder = Create(options);
        recorder.Start();

        _capture.Feed(400);
        _ptt.Press();
        _capture.Feed(100);
        _ptt.Release();

        _recorded.ShouldBeEmpty();
        _discarded.ShouldHaveSingleItem().Duration.ShouldBe(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Pre_roll_is_not_reused_by_the_next_press()
    {
        var options = NoPreRoll with { PreRoll = TimeSpan.FromMilliseconds(200) };
        using var recorder = Create(options);
        recorder.Start();

        _capture.Feed(200, value: 5);
        _ptt.Press();
        _capture.Feed(400, value: 1);
        _ptt.Release();
        _ptt.Press();
        _capture.Feed(400, value: 2);
        _ptt.Release();

        _recorded.Count.ShouldBe(2);
        _recorded[1].Pcm.ShouldAllBe(b => b == 2, "the second press has no audio before it, so no pre-roll");
    }

    [Fact]
    public void Pressing_twice_without_releasing_does_not_restart_the_recording()
    {
        using var recorder = Create();
        recorder.Start();

        _ptt.Press();
        _capture.Feed(400);
        _ptt.Press();
        _capture.Feed(400);
        _ptt.Release();

        _recorded.ShouldHaveSingleItem().Duration.ShouldBe(TimeSpan.FromMilliseconds(800));
    }

    [Fact]
    public void Releasing_without_pressing_does_nothing()
    {
        using var recorder = Create();
        recorder.Start();

        _ptt.Release();

        _recorded.ShouldBeEmpty();
        _discarded.ShouldBeEmpty();
    }

    [Fact]
    public void Disposing_stops_everything_and_ignores_later_events()
    {
        var recorder = Create();
        recorder.Start();
        _ptt.Press();
        _capture.Feed(400);

        recorder.Dispose();
        _ptt.Release();
        _capture.Feed(400);

        _capture.Started.ShouldBeFalse();
        _ptt.Started.ShouldBeFalse();
        _capture.Disposed.ShouldBeFalse("the recorder does not own the devices it was given");
        _recorded.ShouldBeEmpty();
    }

    [Fact]
    public void The_recording_keeps_its_own_copy_of_the_audio()
    {
        using var recorder = Create();
        recorder.Start();
        var buffer = new byte[AudioFormat.Speech.BytesFor(TimeSpan.FromMilliseconds(400))];
        Array.Fill(buffer, (byte)3);

        _ptt.Press();
        _capture.RaiseData(buffer);
        Array.Fill(buffer, (byte)0); // the capture reuses its buffer
        _ptt.Release();

        _recorded.ShouldHaveSingleItem().Pcm.ShouldAllBe(b => b == 3);
    }

    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(300, 200, 0)]
    [InlineData(-1, 1000, 0)]
    [InlineData(300, 1000, -1)]
    public void Invalid_options_are_rejected(int minMs, int maxMs, int preRollMs)
    {
        var options = new SpeechRecorderOptions(TimeSpan.FromMilliseconds(minMs), TimeSpan.FromMilliseconds(maxMs), TimeSpan.FromMilliseconds(preRollMs));

        Should.Throw<ArgumentOutOfRangeException>(() => new SpeechRecorder(_capture, _ptt, options));
    }
}
