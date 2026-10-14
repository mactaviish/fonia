using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Phraseology;

// MCA 100-16/2025, Art. 31 and table 9: the word VENTO, the direction in three digits and GRAUS, then the speed and NÓ/NÓS,
// every digit said apart.
public class SpokenWindTests
{
    [Theory]
    [InlineData(220, 8, "VENTO DOIS DOIS ZERO GRAUS, OITO NÓS")]
    [InlineData(95, 10, "VENTO ZERO NOVE CINCO GRAUS, UNO ZERO NÓS")]
    [InlineData(150, 15, "VENTO UNO CINCO ZERO GRAUS, UNO CINCO NÓS")]
    [InlineData(360, 12, "VENTO TRÊS MEIA ZERO GRAUS, UNO DOIS NÓS")]
    [InlineData(5, 7, "VENTO ZERO ZERO CINCO GRAUS, SETE NÓS")]
    public void Direction_and_speed_are_said_digit_by_digit(int direction, int speed, string expected)
    {
        SpokenText.Wind(new Wind(direction, speed)).ShouldBe(expected);
    }

    [Fact]
    public void North_is_three_six_zero_never_zero()
    {
        SpokenText.Wind(new Wind(0, 10)).ShouldBe("VENTO TRÊS MEIA ZERO GRAUS, UNO ZERO NÓS");
    }

    [Fact]
    public void A_speed_of_one_knot_is_in_the_singular()
    {
        SpokenText.Wind(new Wind(100, 1)).ShouldBe("VENTO UNO ZERO ZERO GRAUS, UNO NÓ");
    }

    [Theory]
    [InlineData(0)]
    public void Less_than_a_knot_is_calm(int speed)
    {
        SpokenText.Wind(new Wind(220, speed)).ShouldBe("VENTO CALMO");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(102)]
    [InlineData(150)]
    public void From_a_hundred_knots_up_it_is_above_ninety_nine(int speed)
    {
        SpokenText.Wind(new Wind(220, speed)).ShouldBe("VENTO DOIS DOIS ZERO GRAUS, VENTO SUPERIOR A 99 NÓS");
    }

    [Fact]
    public void Gusts_come_after_the_speed()
    {
        SpokenText.Wind(new Wind(220, 50, 70)).ShouldBe("VENTO DOIS DOIS ZERO GRAUS, CINCO ZERO NÓS, RAJADAS DE SETE ZERO NÓS");
    }

    [Fact]
    public void Gusts_of_a_hundred_knots_or_more_are_above_ninety_nine_too()
    {
        SpokenText.Wind(new Wind(220, 60, 105)).ShouldBe("VENTO DOIS DOIS ZERO GRAUS, MEIA ZERO NÓS, RAJADAS SUPERIORES A 99 NÓS");
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(361, 10)]
    [InlineData(220, -1)]
    public void A_wind_that_cannot_exist_is_refused(int direction, int speed)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Wind(direction, speed));
    }

    [Fact]
    public void A_gust_below_the_speed_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Wind(220, 20, 10));
    }
}
