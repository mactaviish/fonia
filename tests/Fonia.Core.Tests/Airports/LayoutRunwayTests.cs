using Fonia.Core.Airports;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Airports;

public class LayoutRunwayTests
{
    private static readonly LayoutRunway Runway = TestLayouts.Toy().Runways[1];

    [Theory]
    [InlineData("15/33")]
    [InlineData("33/15")]
    public void A_runway_is_known_by_its_two_designators_in_either_order(string name)
    {
        Runway.Matches(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData("11/29")]
    [InlineData("15")]
    [InlineData("15/33/05")]
    [InlineData("15/29")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_the_runway(string? name)
    {
        Runway.Matches(name).ShouldBeFalse();
    }

    [Fact]
    public void Two_names_of_a_runway_are_the_same_when_they_have_the_same_designators()
    {
        LayoutRunway.SameName("11L/29R", "29R/11L").ShouldBeTrue();
        LayoutRunway.SameName("11L/29R", "11R/29L").ShouldBeFalse();
        LayoutRunway.SameName(null, null).ShouldBeFalse();
    }
}
