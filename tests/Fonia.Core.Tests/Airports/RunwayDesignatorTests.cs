using Fonia.Core.Airports;

namespace Fonia.Core.Tests.Airports;

public class RunwayDesignatorTests
{
    [Theory]
    [InlineData("15", 15, null)]
    [InlineData("09", 9, null)]
    [InlineData("9", 9, null)]
    [InlineData("36", 36, null)]
    [InlineData("01", 1, null)]
    [InlineData("15L", 15, 'L')]
    [InlineData("33r", 33, 'R')]
    [InlineData(" 29C ", 29, 'C')]
    public void A_valid_designator_has_a_number_and_an_optional_side(string text, int number, char? side)
    {
        RunwayDesignator.TryParse(text, out var parsedNumber, out var parsedSide).ShouldBeTrue();

        parsedNumber.ShouldBe(number);
        parsedSide.ShouldBe(side);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("00")]
    [InlineData("37")]
    [InlineData("99")]
    [InlineData("H1")] // a helipad
    [InlineData("N")]
    [InlineData("15X")]
    [InlineData("L15")]
    [InlineData("1515")]
    [InlineData("-5")]
    public void Anything_else_is_not_a_runway_designator(string? text)
    {
        RunwayDesignator.TryParse(text, out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("11", 110.0)]
    [InlineData("29", 290.0)]
    [InlineData("15L", 150.0)]
    [InlineData("9", 90.0)]
    [InlineData("36", 360.0)]
    public void The_magnetic_heading_is_the_number_times_ten(string designator, double expected)
    {
        RunwayDesignator.MagneticHeadingDegrees(designator).ShouldBe(expected);
    }

    [Theory]
    [InlineData("H1")]
    [InlineData("")]
    [InlineData(null)]
    public void A_designator_that_is_not_a_runway_has_no_heading(string? designator)
    {
        RunwayDesignator.MagneticHeadingDegrees(designator).ShouldBeNull();
    }

    [Theory]
    [InlineData("9", "09")]
    [InlineData("09", "09")]
    [InlineData("9r", "09R")]
    [InlineData(" 15l ", "15L")]
    [InlineData("36", "36")]
    [InlineData("1", "01")]
    public void Normalizing_gives_two_digits_and_an_upper_case_side(string text, string expected)
    {
        RunwayDesignator.Normalize(text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("h1", "H1")] // not a runway: just trimmed and upper-cased
    [InlineData("  n ", "N")]
    public void Normalizing_something_that_is_not_a_runway_only_trims_and_upper_cases(string text, string expected)
    {
        RunwayDesignator.Normalize(text).ShouldBe(expected);
    }

    [Theory]
    [InlineData("15", "15", true)]
    [InlineData("9", "09", true)]
    [InlineData("09R", "9r", true)]
    [InlineData("15L", "15R", false)]
    [InlineData("15", "33", false)]
    public void Two_spellings_of_the_same_runway_are_equal(string a, string b, bool expected)
    {
        RunwayDesignator.AreSame(a, b).ShouldBe(expected);
    }
}
