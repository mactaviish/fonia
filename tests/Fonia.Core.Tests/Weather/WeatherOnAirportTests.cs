using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;
using Fonia.Core.Weather;

namespace Fonia.Core.Tests.Weather;

// The METAR on the airport: the wind the controller says (magnetic), the altimeter setting, and the runway the wind makes the one in use.
public class WeatherOnAirportTests
{
    // Two runways 11/29 and 15/33, as in Curitiba: a true heading 19 degrees less than the magnetic one (the variation is 19 W).
    private static RunwayEnd End(string designator, double trueHeading) =>
        new(designator, RunwayDesignator.MagneticHeadingDegrees(designator), trueHeading, null);

    private static Runway Pair(string low, double lowTrue, string high, double highTrue, int length) =>
        new(End(low, lowTrue), End(high, highTrue), length, 150, "ASP", true, false);

    private static readonly Airport Curitiba = new(
        "SBXX", "Curitiba", "Curitiba", AirportType.Large, -25.5, -49.1, 2000, "SBXX", null, "Curitiba", SpokenNameSource.Reference, true,
        [Pair("15", 131, "33", 311, 6000), Pair("11", 91, "29", 271, 9000)],
        [TestAirports.Frequency(AirportFrequencyType.Ground, 118000), TestAirports.Frequency(AirportFrequencyType.Tower, 119500)]);

    private static Metar Metar(Wind? wind, int? qnh = 1018) => new("SBXX", wind, null, qnh, false, "raw");

    [Fact]
    public void The_variation_is_what_the_runways_say_between_their_true_and_magnetic_headings()
    {
        MagneticVariation.EastDegrees(Curitiba).ShouldBe(-19, 1);
    }

    [Fact]
    public void An_airport_without_true_headings_has_no_variation_to_apply()
    {
        MagneticVariation.EastDegrees(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)])).ShouldBe(0);
    }

    [Fact]
    public void The_wind_of_the_metar_is_true_and_the_controller_says_it_magnetic_to_the_ten_degrees()
    {
        var context = AirportContext.Create(Curitiba).WithWeather(Metar(new Wind(90, 8)), keepRunway: false);

        // 090 true with 19 W is 109 magnetic, said as 110.
        context.Wind.ShouldBe(new Wind(110, 8));
    }

    [Fact]
    public void Calm_and_gusts_survive_the_conversion()
    {
        AirportContext.Create(Curitiba).WithWeather(Metar(new Wind(0, 0)), keepRunway: false).Wind.ShouldBe(new Wind(0, 0));
        AirportContext.Create(Curitiba).WithWeather(Metar(new Wind(180, 15, 28)), keepRunway: false).Wind.ShouldBe(new Wind(200, 15, 28));
    }

    [Fact]
    public void The_qnh_is_taken()
    {
        AirportContext.Create(Curitiba).WithWeather(Metar(new Wind(90, 8), 1018), keepRunway: false).Qnh.ShouldBe(1018);
        AirportContext.Create(Curitiba).WithWeather(Metar(null, null), keepRunway: false).Qnh.ShouldBeNull();
    }

    [Theory]
    [InlineData(80, "11")]
    [InlineData(260, "29")]
    [InlineData(150, "15")]
    [InlineData(340, "33")]
    public void The_runway_in_use_is_the_one_with_the_wind_in_the_nose(int trueWindDirection, string expected)
    {
        // The wind is true: the runway headings are magnetic, 19 degrees more, which the conversion takes into account.
        var context = AirportContext.Create(Curitiba).WithWeather(Metar(new Wind(trueWindDirection, 12)), keepRunway: false);

        context.RunwayInUse.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 2)]
    public void With_calm_or_almost_no_wind_the_default_runway_stays(int direction, int speed)
    {
        var plain = AirportContext.Create(Curitiba);

        AirportContext.Create(Curitiba).WithWeather(Metar(new Wind(direction, speed)), keepRunway: false).RunwayInUse.ShouldBe(plain.RunwayInUse);
    }

    [Fact]
    public void A_runway_that_the_user_asked_for_is_kept()
    {
        var asked = AirportContext.Create(Curitiba, "29");

        asked.WithWeather(Metar(new Wind(80, 20)), keepRunway: true).RunwayInUse.ShouldBe("29");
    }

    [Fact]
    public void Without_a_wind_the_runway_does_not_change_and_the_qnh_still_comes()
    {
        var plain = AirportContext.Create(Curitiba);

        var context = plain.WithWeather(Metar(null), keepRunway: false);

        context.RunwayInUse.ShouldBe(plain.RunwayInUse);
        context.Wind.ShouldBeNull();
        context.Qnh.ShouldBe(1018);
    }

    [Fact]
    public void A_closed_runway_is_never_chosen()
    {
        var closed = Curitiba with { Runways = [Pair("15", 131, "33", 311, 6000) with { Closed = true }, Pair("11", 91, "29", 271, 9000)] };

        AirportContext.Create(closed).WithWeather(Metar(new Wind(150, 15)), keepRunway: false).RunwayInUse.ShouldBeOneOf("11", "29");
    }

    // The altimeter setting where the manual asks for it: the taxi (Art. 125) and the circuit (Art. 129).
    [Fact]
    public void The_taxi_and_the_circuit_clearances_say_the_altimeter_setting_when_there_is_one()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext("PRABC", AirportContext.Create(Curitiba));

        phraseology.Speak(new TaxiClearance("15", 1018), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO CINCO, ajuste de altímetro UNO ZERO UNO OITO.");
        phraseology.Speak(new TaxiClearance("15"), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, autorizado táxi para o ponto de espera da PISTA UNO CINCO.");
        phraseology.Speak(new CircuitClearance("15", new Wind(110, 8), 1018), context)!.Text.ShouldBe(
            "PAPA ROMEO ALFA BRAVO CHARLIE, autorizado para o circuito de tráfego, PISTA UNO CINCO, VENTO UNO UNO ZERO GRAUS, OITO NÓS, ajuste de altímetro UNO ZERO UNO OITO, reporte perna do vento.");
    }

    [Fact]
    public void The_engine_puts_the_qnh_of_the_airport_into_the_taxi_and_the_circuit()
    {
        var onSolo = AirportContext.Create(Curitiba).WithWeather(Metar(new Wind(90, 8)), keepRunway: false);
        var engine = new AtcEngine();

        engine.Handle(new Fonia.Core.Nlu.RequestTaxi("PRABC", null), new FlightContext("PRABC", FlightPhase.Parked, "SBXX", null), onSolo)
            .Instruction.ShouldBe(new TaxiClearance(onSolo.RunwayInUse!, 1018));

        var onTower = FrequencyResolver.Apply(onSolo, new Frequency(119500));

        engine.Handle(new Fonia.Core.Nlu.RequestLanding("PRABC"), new FlightContext("PRABC", FlightPhase.Airborne, "SBXX", null), onTower)
            .Instruction.ShouldBe(new CircuitClearance(onSolo.RunwayInUse!, new Wind(110, 8), 1018));
    }
}
