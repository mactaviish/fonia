using System.Text;
using Fonia.Core.Audio;

namespace Fonia.Core.Tests.Audio;

public class WavDecoderTests
{
    private static byte[] Wav(byte[] pcm, AudioFormat? format = null) =>
        WavEncoder.Encode(new RecordedSpeech(format ?? AudioFormat.Speech, pcm));

    [Fact]
    public void It_reads_back_what_the_encoder_wrote()
    {
        byte[] pcm = [1, 2, 3, 4, 5, 6, 7, 8];

        var speech = WavDecoder.Decode(Wav(pcm));

        speech.Format.ShouldBe(AudioFormat.Speech);
        speech.Pcm.ShouldBe(pcm);
    }

    [Fact]
    public void It_keeps_the_format_of_the_file()
    {
        var stereo = new AudioFormat(44_100, 2, 16);

        WavDecoder.Decode(Wav(new byte[16], stereo)).Format.ShouldBe(stereo);
    }

    [Fact]
    public void It_skips_chunks_it_does_not_know_before_the_data()
    {
        var plain = Wav([9, 9, 9, 9]);
        var list = new List<byte>(plain.Take(36));
        list.AddRange("LIST"u8.ToArray());
        list.AddRange(BitConverter.GetBytes(5)); // odd size: chunks are padded to even
        list.AddRange("abcde\0"u8.ToArray());
        list.AddRange(plain.Skip(36));

        var speech = WavDecoder.Decode([.. list]);

        speech.Pcm.ShouldBe([9, 9, 9, 9]);
    }

    [Fact]
    public void A_data_chunk_that_claims_more_than_the_file_has_is_cut_to_the_file()
    {
        var wav = Wav([1, 2, 3, 4, 5, 6, 7, 8]);
        BitConverter.GetBytes(1000).CopyTo(wav, 40); // what a recorder that crashed leaves behind

        WavDecoder.Decode(wav).Pcm.ShouldBe([1, 2, 3, 4, 5, 6, 7, 8]);
    }

    [Fact]
    public void A_streamed_file_with_unknown_length_reads_to_the_end()
    {
        var wav = Wav([1, 2, 3, 4]);
        BitConverter.GetBytes(-1).CopyTo(wav, 40); // 0xFFFFFFFF, as streaming encoders write it

        WavDecoder.Decode(wav).Pcm.ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void Something_that_is_not_a_wav_is_refused_with_a_clear_message()
    {
        var error = Should.Throw<InvalidDataException>(() => WavDecoder.Decode(Encoding.ASCII.GetBytes("this is not audio at all, not even close to it")));

        error.Message.ShouldContain("WAV");
    }

    [Fact]
    public void A_file_too_short_for_a_header_is_refused()
    {
        Should.Throw<InvalidDataException>(() => WavDecoder.Decode(new byte[10]));
    }

    [Fact]
    public void Compressed_or_float_audio_is_refused()
    {
        var wav = Wav(new byte[8]);
        BitConverter.GetBytes((short)3).CopyTo(wav, 20); // IEEE float

        Should.Throw<InvalidDataException>(() => WavDecoder.Decode(wav)).Message.ShouldContain("PCM");
    }

    [Fact]
    public void A_file_without_the_data_chunk_is_refused()
    {
        Should.Throw<InvalidDataException>(() => WavDecoder.Decode(Wav([1, 2]).Take(36).ToArray()));
    }
}
