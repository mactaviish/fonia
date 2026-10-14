namespace Fonia.Core.Speech;

/// <summary>The band of a small aeronautical radio: what is above and below it is cut.</summary>
public static class RadioFilter
{
    // The two sections of a fourth order Butterworth filter: their Q factors.
    private static readonly double[] ButterworthQ = [0.5411961, 1.3065630];

    /// <summary>
    /// Passes the frequencies between <paramref name="lowHz"/> and <paramref name="highHz"/>: a high-pass and a
    /// low-pass of the fourth order, 24 dB per octave, 3 dB down at each edge.
    /// </summary>
    public static float[] BandPass(float[] samples, int sampleRate, double lowHz, double highHz)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lowHz, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(highHz, lowHz);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(highHz, sampleRate / 2.0);

        var data = Array.ConvertAll(samples, s => (double)s);

        foreach (var q in ButterworthQ)
        {
            data = new Biquad(sampleRate, lowHz, q, highPass: true).Process(data);
            data = new Biquad(sampleRate, highHz, q, highPass: false).Process(data);
        }

        return Array.ConvertAll(data, d => (float)d);
    }

    // One second-order section (the "RBJ cookbook" form), run in transposed direct form II.
    private sealed class Biquad
    {
        private readonly double _b0;
        private readonly double _b1;
        private readonly double _b2;
        private readonly double _a1;
        private readonly double _a2;

        public Biquad(int sampleRate, double frequency, double q, bool highPass)
        {
            var w0 = 2 * Math.PI * frequency / sampleRate;
            var cos = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2 * q);
            var a0 = 1 + alpha;

            var edge = highPass ? 1 + cos : 1 - cos;
            _b0 = edge / 2 / a0;
            _b1 = (highPass ? -edge : edge) / a0;
            _b2 = edge / 2 / a0;
            _a1 = -2 * cos / a0;
            _a2 = (1 - alpha) / a0;
        }

        public double[] Process(double[] input)
        {
            var output = new double[input.Length];
            double z1 = 0;
            double z2 = 0;

            for (var i = 0; i < input.Length; i++)
            {
                var x = input[i];
                var y = (_b0 * x) + z1;
                z1 = (_b1 * x) - (_a1 * y) + z2;
                z2 = (_b2 * x) - (_a2 * y);
                output[i] = y;
            }

            return output;
        }
    }
}
