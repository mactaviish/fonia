using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Sim;

public class FrequencyTests
{
    [Fact]
    public void FromMhz_converts_to_exact_khz()
    {
        Frequency.FromMhz(118.275).KHz.ShouldBe(118275);
        Frequency.FromMhz(121.5).KHz.ShouldBe(121500);
    }

    [Fact]
    public void FromMhz_rounds_floating_point_noise()
    {
        // 118.1 * 1000 = 118099.99999999999 in IEEE 754
        Frequency.FromMhz(118.1).KHz.ShouldBe(118100);
    }

    [Fact]
    public void Same_khz_is_equal()
    {
        new Frequency(118275).ShouldBe(Frequency.FromMhz(118.275));
    }

    [Fact]
    public void Mhz_returns_value_in_megahertz()
    {
        new Frequency(121500).MHz.ShouldBe(121.5);
    }

    [Theory]
    [InlineData(118275, "118.275")]
    [InlineData(121500, "121.500")]
    [InlineData(118005, "118.005")]
    public void ToString_uses_three_decimals_with_dot(int khz, string expected)
    {
        new Frequency(khz).ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_khz_is_rejected(int khz)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Frequency(khz));
    }
}
