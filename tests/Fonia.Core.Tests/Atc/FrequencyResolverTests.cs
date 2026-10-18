using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

public class FrequencyResolverTests
{
    private static readonly AirportContext WithSoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static Frequency Of(AirportContext airport, AirportFrequencyType type) => airport.Airport.FrequencyOf(type)!.Frequency;

    [Fact]
    public void On_the_frequency_of_the_solo_the_solo_answers()
    {
        var context = FrequencyResolver.Apply(WithSoloAndTower, Of(WithSoloAndTower, AirportFrequencyType.Ground));

        context.Unit.ShouldBe(ServiceUnit.Ground);
        context.Tuned.ShouldBe(Of(WithSoloAndTower, AirportFrequencyType.Ground));
    }

    [Fact]
    public void On_the_frequency_of_the_torre_the_torre_answers_even_when_the_airport_has_a_solo()
    {
        var context = FrequencyResolver.Apply(WithSoloAndTower, Of(WithSoloAndTower, AirportFrequencyType.Tower));

        context.Unit.ShouldBe(ServiceUnit.Tower);
    }

    [Fact]
    public void On_the_frequency_of_the_afis_the_afis_answers()
    {
        var afis = AirportContext.Create(TestAirports.Airport("SBAF", [TestAirports.Runway("09", "27", 4000)], AirportFrequencyType.Afis));

        FrequencyResolver.Apply(afis, Of(afis, AirportFrequencyType.Afis)).Unit.ShouldBe(ServiceUnit.Afis);
    }

    [Theory]
    [InlineData(121500)]
    [InlineData(118000)]
    [InlineData(123450)]
    public void On_a_frequency_that_is_not_the_airports_nobody_answers_and_the_tuned_one_is_kept_for_the_message(int kHz)
    {
        // 118.000 is the Solo's of the test airport; the other two are nobody's.
        var tuned = new Frequency(kHz);

        var context = FrequencyResolver.Apply(WithSoloAndTower, tuned);

        context.Tuned.ShouldBe(tuned);
        context.Unit.ShouldBe(kHz == 118000 ? ServiceUnit.Ground : ServiceUnit.None);
    }

    [Fact]
    public void Without_a_radio_reading_the_airports_own_unit_stays()
    {
        var context = FrequencyResolver.Apply(WithSoloAndTower, null);

        context.ShouldBe(WithSoloAndTower);
        context.Tuned.ShouldBeNull();
    }

    [Fact]
    public void Only_the_frequencies_of_a_unit_that_talks_count_not_the_atis()
    {
        var airport = TestAirports.Airport(
            "SBXX",
            [TestAirports.Runway("15", "33", 6000)],
            AirportFrequencyType.Ground,
            AirportFrequencyType.Atis);
        var context = AirportContext.Create(airport);

        FrequencyResolver.Apply(context, Of(context, AirportFrequencyType.Atis)).Unit.ShouldBe(ServiceUnit.None);
    }

    [Fact]
    public void On_the_frequency_of_the_approach_the_controle_answers()
    {
        var airport = AirportContext.Create(TestAirports.Airport(
            "SBXX",
            [TestAirports.Runway("15", "33", 6000)],
            AirportFrequencyType.Tower,
            AirportFrequencyType.Approach));

        var context = FrequencyResolver.Apply(airport, Of(airport, AirportFrequencyType.Approach));

        context.Unit.ShouldBe(ServiceUnit.Approach);
        context.Tuned.ShouldBe(Of(airport, AirportFrequencyType.Approach));
    }

    [Fact]
    public void Every_approach_frequency_of_the_airport_is_the_controle()
    {
        var runways = new[] { TestAirports.Runway("15", "33", 6000) };
        var first = TestAirports.Frequency(AirportFrequencyType.Approach, 119950);
        var second = TestAirports.Frequency(AirportFrequencyType.Approach, 119750);
        var airport = AirportContext.Create(TestAirports.Airport("SBXX", runways, AirportFrequencyType.Tower) with { Frequencies = [first, second] });

        FrequencyResolver.Apply(airport, first.Frequency).Unit.ShouldBe(ServiceUnit.Approach);
        FrequencyResolver.Apply(airport, second.Frequency).Unit.ShouldBe(ServiceUnit.Approach);
    }

    [Fact]
    public void The_approach_is_never_the_unit_an_airport_starts_with()
    {
        var airport = TestAirports.Airport("SBXX", [TestAirports.Runway("15", "33", 6000)], AirportFrequencyType.Approach);

        AirportContext.Create(airport).Unit.ShouldBe(ServiceUnit.None);
    }

    [Fact]
    public void The_runway_in_use_is_kept()
    {
        var context = FrequencyResolver.Apply(AirportContext.Create(TestAirports.WithSolo(), "15"), Of(WithSoloAndTower, AirportFrequencyType.Tower));

        context.RunwayInUse.ShouldBe("15");
    }
}
