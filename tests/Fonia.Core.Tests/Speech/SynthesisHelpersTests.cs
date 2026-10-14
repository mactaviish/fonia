using Fonia.Core.Audio;
using Fonia.Core.Speech;

namespace Fonia.Core.Tests.Speech;

public class SynthesisHelpersTests
{
    private const int Rate = 24_000;

    [Fact]
    public void Synthesized_speech_knows_its_duration()
    {
        new SynthesizedSpeech(new float[Rate * 3 / 2], Rate).Duration.ShouldBe(TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void It_converts_to_16_bit_pcm_for_playback_or_a_wav_file()
    {
        var speech = new SynthesizedSpeech([0f, 0.5f, -0.5f, 1f, -1f, 2f, -2f], Rate);

        var pcm = speech.ToPcm16();

        pcm.Format.ShouldBe(new AudioFormat(Rate, 1, 16));
        var samples = Enumerable.Range(0, pcm.Pcm.Length / 2).Select(i => BitConverter.ToInt16(pcm.Pcm, i * 2)).ToArray();
        samples.ShouldBe([0, 16384, -16384, 32767, -32767, 32767, -32767]);
    }

    [Fact]
    public void The_pcm_goes_into_a_wav_that_the_decoder_reads_back()
    {
        var wav = WavEncoder.Encode(new SynthesizedSpeech([0f, 0.25f], Rate).ToPcm16());

        WavDecoder.Decode(wav).Format.SampleRate.ShouldBe(Rate);
    }

    [Fact]
    public void Silence_around_the_voice_is_trimmed_leaving_a_little_to_breathe()
    {
        var samples = new float[Rate * 3];
        for (var i = Rate; i < Rate * 2; i++)
        {
            samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / Rate));
        }

        var trimmed = SilenceTrimmer.Trim(samples, Rate);

        // 1 s of voice plus about 30 ms before and 80 ms after.
        trimmed.Length.ShouldBeInRange((int)(Rate * 1.09), (int)(Rate * 1.13));
        Math.Abs(trimmed[0]).ShouldBeLessThan(0.01f);
    }

    [Fact]
    public void A_tail_of_faint_noise_counts_as_silence()
    {
        var samples = new float[Rate * 2];
        for (var i = 0; i < Rate; i++)
        {
            samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / Rate));
        }

        for (var i = Rate; i < samples.Length; i++)
        {
            samples[i] = 0.0005f;
        }

        SilenceTrimmer.Trim(samples, Rate).Length.ShouldBeLessThan((int)(Rate * 1.2));
    }

    [Fact]
    public void Nothing_to_trim_is_returned_whole_and_all_silence_is_empty()
    {
        var voice = Enumerable.Repeat(0.5f, 100).ToArray();

        SilenceTrimmer.Trim(voice, Rate).Length.ShouldBe(100);
        SilenceTrimmer.Trim(new float[Rate], Rate).ShouldBeEmpty();
        SilenceTrimmer.Trim([], Rate).ShouldBeEmpty();
    }

    [Fact]
    public void A_short_text_is_one_chunk()
    {
        PhonemeChunker.Split("pˈapæ xomˈeʊ.", 510).ShouldBe(["pˈapæ xomˈeʊ."]);
    }

    [Fact]
    public void A_long_text_is_cut_after_the_sentences()
    {
        var chunks = PhonemeChunker.Split("aaaa bbbb. cccc dddd. eeee.", 12);

        chunks.ShouldBe(["aaaa bbbb.", "cccc dddd.", "eeee."]);
    }

    [Fact]
    public void A_sentence_longer_than_the_limit_is_cut_after_the_commas_then_the_words()
    {
        PhonemeChunker.Split("aaaa, bbbb, cccc", 6).ShouldBe(["aaaa,", "bbbb,", "cccc"]);
        PhonemeChunker.Split("aaaa bbbb cccc", 9).ShouldBe(["aaaa bbbb", "cccc"]);
    }

    [Fact]
    public void A_word_longer_than_the_limit_is_cut_hard_and_nothing_is_lost()
    {
        var chunks = PhonemeChunker.Split("abcdefghij", 4);

        chunks.ShouldAllBe(c => c.Length <= 4);
        string.Concat(chunks).ShouldBe("abcdefghij");
    }

    [Fact]
    public void No_chunk_exceeds_the_limit_and_the_words_survive_in_order()
    {
        var text = string.Join(' ', Enumerable.Range(0, 300).Select(i => $"w{i}")) + ".";

        var chunks = PhonemeChunker.Split(text, 100);

        chunks.ShouldAllBe(c => c.Length <= 100);
        string.Join(' ', chunks).ShouldBe(text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_say_gives_no_chunks(string text)
    {
        PhonemeChunker.Split(text, 100).ShouldBeEmpty();
    }
}
