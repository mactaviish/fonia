using Fonia.Core.Speech;

namespace Fonia.Core.Tests.Speech;

public class RadioEffectTests
{
    private const int Rate = 24_000;

    private static float[] Tone(double hertz, double seconds = 1.0, double amplitude = 0.5)
    {
        var samples = new float[(int)(Rate * seconds)];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hertz * i / Rate));
        }

        return samples;
    }

    private static double Rms(float[] samples, int from, int count)
    {
        double sum = 0;

        for (var i = from; i < from + count; i++)
        {
            sum += samples[i] * samples[i];
        }

        return Math.Sqrt(sum / count);
    }

    private static double Peak(float[] samples) => samples.Max(Math.Abs);

    // Gain of the filter alone, measured on the middle of a tone, away from the start-up of the filter.
    private static double Gain(double hertz, double low = 350, double high = 3200)
    {
        var input = Tone(hertz);
        var output = RadioFilter.BandPass(input, Rate, low, high);

        return Rms(output, Rate / 4, Rate / 2) / Rms(input, Rate / 4, Rate / 2);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1500)]
    [InlineData(2000)]
    public void The_band_passes_the_voice_untouched(double hertz)
    {
        Gain(hertz).ShouldBeInRange(0.85, 1.15);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(100)]
    public void The_band_cuts_the_low_end_that_a_small_radio_cannot_reproduce(double hertz)
    {
        Gain(hertz).ShouldBeLessThan(0.05);
    }

    [Theory]
    [InlineData(6000)]
    [InlineData(9000)]
    public void The_band_cuts_the_high_end(double hertz)
    {
        Gain(hertz).ShouldBeLessThan(0.1);
    }

    [Fact]
    public void The_band_edges_are_about_where_they_were_asked()
    {
        // A fourth order filter is 3 dB (0.707) down at its corner.
        Gain(350).ShouldBeInRange(0.55, 0.85);
        Gain(3200).ShouldBeInRange(0.55, 0.85);
    }

    [Fact]
    public void A_band_wider_than_the_sampling_allows_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RadioFilter.BandPass(Tone(1000), Rate, 350, Rate));
        Should.Throw<ArgumentOutOfRangeException>(() => RadioFilter.BandPass(Tone(1000), Rate, 3200, 350));
    }

    [Fact]
    public void Turned_off_it_returns_the_voice_as_it_is()
    {
        var input = Tone(1000);

        var output = RadioEffect.Apply(input, Rate, RadioIntensity.Off, new Random(1));

        output.ShouldBe(input);
        output.ShouldNotBeSameAs(input);
    }

    [Theory]
    [InlineData(RadioIntensity.Light)]
    [InlineData(RadioIntensity.Medium)]
    [InlineData(RadioIntensity.Strong)]
    public void The_effect_adds_the_key_click_before_and_the_squelch_after_and_normalizes_the_level(RadioIntensity intensity)
    {
        var input = Tone(1000);

        var output = RadioEffect.Apply(input, Rate, intensity, new Random(1));

        output.Length.ShouldBeGreaterThan(input.Length);
        Peak(output).ShouldBe(0.9, 0.001);
        output.ShouldAllBe(s => float.IsFinite(s));
        // The first instants are still: the click comes after a short silence, not at sample zero.
        output.Take(Rate / 100).ShouldAllBe(s => Math.Abs(s) < 0.001f);
    }

    [Fact]
    public void The_same_random_gives_the_same_sound_and_another_gives_another_hiss()
    {
        var input = Tone(1000);

        var a = RadioEffect.Apply(input, Rate, RadioIntensity.Medium, new Random(5));
        var b = RadioEffect.Apply(input, Rate, RadioIntensity.Medium, new Random(5));
        var c = RadioEffect.Apply(input, Rate, RadioIntensity.Medium, new Random(6));

        a.ShouldBe(b);
        a.ShouldNotBe(c);
    }

    [Fact]
    public void A_stronger_setting_squeezes_the_voice_more()
    {
        var input = Tone(1000);

        double Crest(RadioIntensity intensity)
        {
            var output = RadioEffect.Apply(input, Rate, intensity, new Random(1));
            return Peak(output) / Rms(output, Rate / 4, Rate / 2);
        }

        Crest(RadioIntensity.Strong).ShouldBeLessThan(Crest(RadioIntensity.Light));
    }

    [Fact]
    public void Silence_gives_only_the_noise_and_never_divides_by_zero()
    {
        var output = RadioEffect.Apply(new float[Rate], Rate, RadioIntensity.Medium, new Random(1));

        output.ShouldAllBe(s => float.IsFinite(s));
        Peak(output).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void An_empty_voice_gives_an_empty_result()
    {
        RadioEffect.Apply([], Rate, RadioIntensity.Medium, new Random(1)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("desligado", RadioIntensity.Off)]
    [InlineData("OFF", RadioIntensity.Off)]
    [InlineData("leve", RadioIntensity.Light)]
    [InlineData("media", RadioIntensity.Medium)]
    [InlineData("média", RadioIntensity.Medium)]
    [InlineData("forte", RadioIntensity.Strong)]
    [InlineData("strong", RadioIntensity.Strong)]
    public void The_names_the_user_may_type_are_understood(string text, RadioIntensity expected)
    {
        RadioEffect.TryParse(text, out var intensity).ShouldBeTrue();
        intensity.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("altíssimo")]
    [InlineData(null)]
    public void An_unknown_name_is_not_understood(string? text)
    {
        RadioEffect.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void Medium_is_the_default()
    {
        RadioEffect.DefaultIntensity.ShouldBe(RadioIntensity.Medium);
    }
}
