using Fonia.Core.Audio;
using Fonia.Core.Speech;

namespace Fonia.Core.Tests.Speech;

public class PcmConverterTests
{
    private static RecordedSpeech Speech(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new RecordedSpeech(AudioFormat.Speech, bytes);
    }

    [Fact]
    public void It_scales_16_bit_samples_to_minus_one_to_one()
    {
        var floats = PcmConverter.ToFloat(Speech(0, 16384, -16384, short.MaxValue, short.MinValue));

        floats.Length.ShouldBe(5);
        floats[0].ShouldBe(0f);
        floats[1].ShouldBe(0.5f, 0.0001f);
        floats[2].ShouldBe(-0.5f, 0.0001f);
        floats[3].ShouldBe(1f, 0.0001f);
        floats[4].ShouldBe(-1f);
    }

    [Fact]
    public void An_empty_recording_gives_no_samples()
    {
        PcmConverter.ToFloat(Speech()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(8_000, 1, 16)]
    [InlineData(16_000, 2, 16)]
    [InlineData(16_000, 1, 8)]
    public void Only_the_speech_format_is_accepted(int rate, int channels, int bits)
    {
        var speech = new RecordedSpeech(new AudioFormat(rate, channels, bits), new byte[64]);

        Should.Throw<ArgumentException>(() => PcmConverter.ToFloat(speech)).Message.ShouldContain("16 kHz");
    }

    [Fact]
    public void A_half_sample_at_the_end_is_rejected_not_silently_dropped()
    {
        var speech = new RecordedSpeech(AudioFormat.Speech, new byte[5]);

        Should.Throw<ArgumentException>(() => PcmConverter.ToFloat(speech));
    }
}
