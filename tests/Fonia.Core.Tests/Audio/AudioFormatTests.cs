using Fonia.Core.Audio;

namespace Fonia.Core.Tests.Audio;

public class AudioFormatTests
{
    [Fact]
    public void The_speech_format_is_what_the_recognizer_takes_16_kHz_mono_16_bit()
    {
        AudioFormat.Speech.SampleRate.ShouldBe(16_000);
        AudioFormat.Speech.Channels.ShouldBe(1);
        AudioFormat.Speech.BitsPerSample.ShouldBe(16);
        AudioFormat.Speech.BytesPerFrame.ShouldBe(2);
        AudioFormat.Speech.BytesPerSecond.ShouldBe(32_000);
    }

    [Fact]
    public void Durations_and_byte_counts_convert_both_ways()
    {
        AudioFormat.Speech.BytesFor(TimeSpan.FromMilliseconds(250)).ShouldBe(8_000);
        AudioFormat.Speech.DurationOf(8_000).ShouldBe(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void Byte_counts_are_whole_frames()
    {
        var stereo = new AudioFormat(44_100, 2, 16);

        stereo.BytesFor(TimeSpan.FromMilliseconds(1)).ShouldBe(176, "44 frames of 4 bytes, rounded down to a whole frame");
        (stereo.BytesFor(TimeSpan.FromMilliseconds(1)) % stereo.BytesPerFrame).ShouldBe(0);
    }

    [Theory]
    [InlineData(0, 1, 16)]
    [InlineData(16_000, 0, 16)]
    [InlineData(16_000, 1, 12)]
    [InlineData(16_000, 1, 0)]
    public void Invalid_formats_are_rejected(int rate, int channels, int bits)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AudioFormat(rate, channels, bits));
    }
}
