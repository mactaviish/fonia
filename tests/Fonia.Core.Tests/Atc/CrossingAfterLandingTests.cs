using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// An aircraft that landed on 15 and vacated it at the holding point of 15 (node 5 of the toy airport) has to cross runway 11/29 to reach the
// apron: the Torre clears the crossing and says where to call after it ("cruze a pista ..., após chame Solo em ..."), whether the pilot reports
// the runway vacated or the telemetry shows it. Without the map or the position of the aircraft nothing changes.
public class CrossingAfterLandingTests
{
    private const string Own = "PRABC";

    private static readonly AirportLayout Toy = TestLayouts.Toy();

    private static readonly AirportContext Solo = AirportContext.Create(TestAirports.WithSolo(), "15") with { Layout = Toy };

    private static readonly Frequency SoloFrequency = Solo.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency;

    private static readonly AirportContext OnTower = FrequencyResolver.Apply(Solo, Solo.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo = FrequencyResolver.Apply(Solo, SoloFrequency);

    private static readonly CrossRunway Crossing = new("11", SoloFrequency);

    private static AircraftState AtNode(int id, bool onGround = true)
    {
        var position = Toy.Nodes.Single(n => n.Id == id).Position;

        return new AircraftState(
            DateTimeOffset.UtcNow, position.LatitudeDegrees, position.LongitudeDegrees, 3000, onGround ? 0 : 500, 10, 150, onGround, null, null, 1200, "PR-ABC", "C172");
    }

    private static FlightContext Landed(int node = 5, FlightPhase phase = FlightPhase.Landed) =>
        new(Own, phase, "SBXX", null, null, null, "15", AtNode(node));

    [Fact]
    public void The_pilot_who_vacated_the_runway_is_cleared_to_cross_the_one_on_the_way_to_the_apron()
    {
        var decision = new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(), OnTower);

        decision.Instruction.ShouldBe(Crossing);
        decision.NextContext.PendingReadback.ShouldBe(Crossing);
        decision.NextContext.LastInstruction.ShouldBe(Crossing);
        decision.NextContext.Phase.ShouldBe(FlightPhase.Landed);
    }

    [Fact]
    public void The_crossing_is_cleared_when_the_pilot_reports_before_the_notice_of_the_landing_too()
    {
        new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(phase: FlightPhase.LandingCleared), OnTower).Instruction.ShouldBe(Crossing);
    }

    [Fact]
    public void The_telemetry_that_shows_the_runway_vacated_clears_the_crossing_without_the_pilot_reporting()
    {
        var vacated = new RunwayVacated(DateTimeOffset.UtcNow, Toy.Nodes.Single(n => n.Id == 5).Position);

        var decision = new AtcEngine().Handle(vacated, Landed(node: 2), OnTower);

        decision.Instruction.ShouldBe(Crossing);
        decision.NextContext.PendingReadback.ShouldBe(Crossing);
    }

    [Fact]
    public void The_telemetry_event_uses_its_own_position_and_falls_back_on_the_aircraft()
    {
        var engine = new AtcEngine();

        engine.Handle(new RunwayVacated(DateTimeOffset.UtcNow, null), Landed(), OnTower).Instruction.ShouldBe(Crossing);
        engine.Handle(new RunwayVacated(DateTimeOffset.UtcNow, null), Landed() with { Aircraft = null }, OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Where_the_way_to_the_apron_crosses_nothing_the_torre_passes_the_flight_to_the_solo_as_before()
    {
        var decision = new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(node: 2), OnTower);

        decision.Instruction.ShouldBe(new HandoffToGround(SoloFrequency));
    }

    [Fact]
    public void Without_the_map_or_the_position_the_torre_passes_the_flight_to_the_solo_as_before()
    {
        var engine = new AtcEngine();

        engine.Handle(new ReportRunwayVacated(Own), Landed(), OnTower with { Layout = null }).Instruction.ShouldBe(new HandoffToGround(SoloFrequency));
        engine.Handle(new ReportRunwayVacated(Own), Landed() with { Aircraft = null }, OnTower).Instruction.ShouldBe(new HandoffToGround(SoloFrequency));
        engine.Handle(new ReportRunwayVacated(Own), Landed() with { Aircraft = AtNode(5, onGround: false) }, OnTower).Instruction.ShouldBe(new HandoffToGround(SoloFrequency));
    }

    [Fact]
    public void The_crossing_is_cleared_once_and_the_report_after_it_passes_the_flight_to_the_solo()
    {
        var engine = new AtcEngine();
        var told = engine.Handle(new ReportRunwayVacated(Own), Landed(), OnTower).NextContext;

        engine.Handle(new ReportRunwayVacated(Own), told, OnTower).Instruction.ShouldBe(new HandoffToGround(SoloFrequency));
        engine.Handle(new RunwayVacated(DateTimeOffset.UtcNow, null), told, OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData(FlightPhase.Airborne)]
    [InlineData(FlightPhase.Taxiing)]
    [InlineData(FlightPhase.InCircuit)]
    public void The_telemetry_event_of_a_flight_that_did_not_land_is_none_of_the_controllers_business(FlightPhase phase)
    {
        new AtcEngine().Handle(new RunwayVacated(DateTimeOffset.UtcNow, null), Landed(phase: phase), OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void A_pilot_who_already_left_the_torres_frequency_hears_nothing_about_the_event()
    {
        new AtcEngine().Handle(new RunwayVacated(DateTimeOffset.UtcNow, null), Landed(), OnSolo).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void The_end_said_is_the_one_that_points_the_way_the_landing_did()
    {
        var onThe33 = AirportContext.Create(TestAirports.WithSolo(), "33") with { Layout = Toy };
        var tower = FrequencyResolver.Apply(onThe33, onThe33.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

        // Landed on 33 (330.2): of 11/29 the 29 (290.5) is the nearer end.
        new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(), tower).Instruction.ShouldBe(new CrossRunway("29", SoloFrequency));
    }

    [Fact]
    public void An_airport_with_the_runways_apart_for_landing_says_the_end_of_the_landing_runway()
    {
        var apart = OnTower with { RunwayInUse = "11", ArrivalRunway = "15" };

        new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(), apart).Instruction.ShouldBe(Crossing);
    }

    // The way to the apron that goes back over the runway just landed on is the graph's nearest node being on the far side: not a crossing.
    [Fact]
    public void A_way_that_goes_back_over_the_runway_landed_on_is_no_crossing_to_clear()
    {
        var landedOn29 = OnTower with { ArrivalRunway = "29" };

        new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(), landedOn29).Instruction.ShouldBe(new HandoffToGround(SoloFrequency));
    }

    [Fact]
    public void The_solo_does_not_clear_runway_crossings_of_the_torre()
    {
        new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(), OnSolo).Instruction.ShouldBeOfType<TaxiToApron>();
    }

    [Fact]
    public void A_torre_without_a_solo_clears_the_crossing_and_says_nothing_about_calling()
    {
        var airport = AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("15", "33", 6000), TestAirports.Runway("29", "11", 9000)], AirportFrequencyType.Tower), "15") with { Layout = Toy };
        var onTower = FrequencyResolver.Apply(airport, airport.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

        new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(), onTower).Instruction.ShouldBe(new CrossRunway("11", null));
    }

    [Theory]
    [InlineData("cruzando pista um um, chamo solo, Papa Romeo Alfa Bravo Charlie", true)]
    [InlineData("cruze pista uno uno após chamo solo, Papa Romeo Alfa Bravo Charlie", true)]
    [InlineData("cruzando, Papa Romeo Alfa Bravo Charlie", true)]
    [InlineData("cruzando pista dois nove, Papa Romeo Alfa Bravo Charlie", false)]
    [InlineData("ciente, Papa Romeo Alfa Bravo Charlie", false)]
    public void The_crossing_is_read_back_with_the_word_cruzar_and_the_right_runway_if_one_is_named(string said, bool correct)
    {
        var told = new AtcEngine().Handle(new ReportRunwayVacated(Own), Landed(), OnTower).NextContext;
        var intent = new RuleBasedNlu().Interpret(said, told);

        var decision = new AtcEngine().Handle(intent, told, OnTower);

        intent.ShouldBeOfType<Readback>();
        (decision.Instruction is NoResponse).ShouldBe(correct);
    }

    [Fact]
    public void The_crossing_is_phrased_as_the_maintainer_gave_it_without_the_name_of_the_airport()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext(Own, OnTower);

        phraseology.Speak(new CrossRunway("10L", new Frequency(126900)), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, cruze a PISTA UNO ZERO ESQUERDA, após chame Solo em UNO DOIS MEIA DECIMAL NOVE.");
        phraseology.Speak(new CrossRunway("10L", null), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, cruze a PISTA UNO ZERO ESQUERDA.");
        phraseology.Speak(new RequestReadbackCorrection(new CrossRunway("10L", null)), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, negativo, cruze a PISTA UNO ZERO ESQUERDA.");
    }
}
