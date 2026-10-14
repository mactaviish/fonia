using System.Text;
using Fonia.Core.Audio;

namespace Fonia.Core.Tests.Audio;

public class WavEncoderTests
{
    [Fact]
    public void It_writes_a_canonical_44_byte_pcm_header_followed_by_the_samples()
    {
        byte[] pcm = [1, 2, 3, 4, 5, 6];

        var wav = WavEncoder.Encode(new RecordedSpeech(AudioFormat.Speech, pcm));

        wav.Length.ShouldBe(44 + pcm.Length);
        Encoding.ASCII.GetString(wav, 0, 4).ShouldBe("RIFF");
        BitConverter.ToInt32(wav, 4).ShouldBe(36 + pcm.Length);
        Encoding.ASCII.GetString(wav, 8, 8).ShouldBe("WAVEfmt ");
        BitConverter.ToInt32(wav, 16).ShouldBe(16);
        BitConverter.ToInt16(wav, 20).ShouldBe((short)1, "PCM");
        BitConverter.ToInt16(wav, 22).ShouldBe((short)1, "mono");
        BitConverter.ToInt32(wav, 24).ShouldBe(16_000);
        BitConverter.ToInt32(wav, 28).ShouldBe(32_000, "byte rate");
        BitConverter.ToInt16(wav, 32).ShouldBe((short)2, "block align");
        BitConverter.ToInt16(wav, 34).ShouldBe((short)16);
        Encoding.ASCII.GetString(wav, 36, 4).ShouldBe("data");
        BitConverter.ToInt32(wav, 40).ShouldBe(pcm.Length);
        wav.Skip(44).ShouldBe(pcm);
    }

    [Fact]
    public void It_encodes_an_empty_recording_as_a_header_only_file()
    {
        WavEncoder.Encode(new RecordedSpeech(AudioFormat.Speech, [])).Length.ShouldBe(44);
    }
}
