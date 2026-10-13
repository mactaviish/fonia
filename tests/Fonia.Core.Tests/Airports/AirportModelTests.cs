using Fonia.Core.Airports;
using Fonia.Core.Sim;

namespace Fonia.Core.Tests.Airports;

public class AirportModelTests
{
    private static Airport Curitiba() => new(
        Ident: "SBCT",
        Name: "Curitiba-Afonso Pena International Airport",
        Municipality: "Curitiba",
        Type: AirportType.Large,
        LatitudeDegrees: -25.5285,
        LongitudeDegrees: -49.1758,
        ElevationFeet: 2988,
        IcaoCode: "SBCT",
        IataCode: "CWB",
        SpokenName: "Curitiba",
        SpokenNameSource: SpokenNameSource.Suggestion,
        SpokenNameReviewed: false,
        Runways: [],
        Frequencies:
        [
            new AirportFrequency(AirportFrequencyType.Approach, Frequency.FromMhz(119.95), "CURITIBA APP"),
            new AirportFrequency(AirportFrequencyType.Ground, Frequency.FromMhz(121.9), "GND"),
            new AirportFrequency(AirportFrequencyType.Tower, Frequency.FromMhz(118.55), "CURITIBA TWR"),
            new AirportFrequency(AirportFrequencyType.Tower, Frequency.FromMhz(118.6), "TWR 2"),
        ]);

    [Fact]
    public void A_frequency_is_found_by_its_type()
    {
        Curitiba().FrequencyOf(AirportFrequencyType.Ground)!.Frequency.ShouldBe(new Frequency(121900));
        Curitiba().FrequencyOf(AirportFrequencyType.Approach)!.Frequency.ShouldBe(new Frequency(119950));
    }

    [Fact]
    public void With_several_of_a_type_the_first_listed_is_returned()
    {
        Curitiba().FrequencyOf(AirportFrequencyType.Tower)!.Frequency.ShouldBe(new Frequency(118550));
    }

    [Fact]
    public void A_type_the_airport_does_not_have_gives_null()
    {
        Curitiba().FrequencyOf(AirportFrequencyType.Afis).ShouldBeNull();
        Curitiba().HasFrequency(AirportFrequencyType.Afis).ShouldBeFalse();
        Curitiba().HasFrequency(AirportFrequencyType.Ground).ShouldBeTrue();
    }

    [Fact]
    public void A_runway_with_both_ends_exposes_them_by_designator()
    {
        var runway = new Runway(
            new RunwayEnd("15", MagneticHeadingDegrees: 150, TrueHeadingDegrees: 136, ElevationFeet: 2987),
            new RunwayEnd("33", MagneticHeadingDegrees: 330, TrueHeadingDegrees: 314, ElevationFeet: 2987),
            LengthFeet: 7277,
            WidthFeet: 148,
            Surface: "ASP",
            Lighted: true,
            Closed: false);

        runway.Ends.Select(end => end.Designator).ShouldBe(["15", "33"]);
        runway.Name.ShouldBe("15/33");
    }
}
