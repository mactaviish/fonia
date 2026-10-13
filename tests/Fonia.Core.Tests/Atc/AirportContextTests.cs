using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

public class AirportContextTests
{
    private static readonly Runway Long = TestAirports.Runway("11", "29", 9000);

    [Fact]
    public void An_airport_with_a_solo_is_answered_by_the_solo()
    {
        AirportContext.Create(TestAirports.WithSolo()).Unit.ShouldBe(ServiceUnit.Ground);
    }

    [Theory]
    [InlineData(new[] { AirportFrequencyType.Tower, AirportFrequencyType.Afis }, ServiceUnit.Tower)]
    [InlineData(new[] { AirportFrequencyType.Afis }, ServiceUnit.Afis)]
    [InlineData(new[] { AirportFrequencyType.Atis, AirportFrequencyType.Approach }, ServiceUnit.None)]
    [InlineData(new AirportFrequencyType[0], ServiceUnit.None)]
    public void Without_a_solo_the_next_service_the_airport_has_answers(AirportFrequencyType[] frequencies, ServiceUnit expected)
    {
        var airport = TestAirports.Airport("SBXX", [Long], frequencies);

        AirportContext.Create(airport).Unit.ShouldBe(expected);
    }

    [Fact]
    public void The_default_runway_is_the_longest_one_on_its_lower_numbered_end()
    {
        AirportContext.Create(TestAirports.WithSolo()).RunwayInUse.ShouldBe("11");
    }

    [Fact]
    public void The_default_ignores_closed_runways_and_those_of_unknown_length_come_last()
    {
        var airport = TestAirports.Airport(
            "SBXX",
            [TestAirports.Runway("09", "27", 12000, closed: true), TestAirports.Runway("15", "33", null), TestAirports.Runway("04", "22", 3000)]);

        AirportContext.Create(airport).RunwayInUse.ShouldBe("04");
    }

    [Theory]
    [InlineData("29", "29")]
    [InlineData("33", "33")]
    [InlineData("09", null)]
    public void The_requested_runway_must_be_an_end_of_one_of_the_airports_runways(string requested, string? expected)
    {
        var airport = TestAirports.Airport("SBXX", [TestAirports.Runway("15", "33", 6000), TestAirports.Runway("29", "11", 9000)]);

        if (expected is null)
        {
            Should.Throw<ArgumentException>(() => AirportContext.Create(airport, requested)).Message.ShouldContain("15/33");
        }
        else
        {
            AirportContext.Create(airport, requested).RunwayInUse.ShouldBe(expected);
        }
    }

    [Fact]
    public void A_requested_runway_may_be_written_loosely()
    {
        var airport = TestAirports.Airport("SBXX", [TestAirports.Runway("09", "27", 6000)]);

        AirportContext.Create(airport, "9").RunwayInUse.ShouldBe("09");
        AirportContext.Create(airport, " 27 ").RunwayInUse.ShouldBe("27");
    }

    [Fact]
    public void A_closed_runway_cannot_be_requested()
    {
        var airport = TestAirports.Airport("SBXX", [TestAirports.Runway("09", "27", 6000, closed: true)]);

        Should.Throw<ArgumentException>(() => AirportContext.Create(airport, "09"));
    }

    [Fact]
    public void An_airport_without_a_usable_runway_has_none_in_use()
    {
        AirportContext.Create(TestAirports.Airport("SBXX", [], AirportFrequencyType.Ground)).RunwayInUse.ShouldBeNull();
    }
}
