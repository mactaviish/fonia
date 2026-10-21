using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// Taxiing out for runway 15 of the toy airport the way goes over runway 11/29: the Solo takes the aircraft to the holding point of 11/29 (node 2)
// and passes it to the Torre, which clears the crossing and only then the line-up. Without the map or the position nothing changes.
public class CrossingBeforeTakeoffTests
{
    private const string Own = "PRABC";

    private static readonly AirportLayout Toy = TestLayouts.Toy();

    private static readonly AirportContext Solo = AirportContext.Create(TestAirports.WithSolo(), "15") with { Layout = Toy };

    private static readonly AirportContext OnTower = FrequencyResolver.Apply(Solo, Solo.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static AircraftState AtNode(int id, bool onGround = true)
    {
        var position = Toy.Nodes.Single(n => n.Id == id).Position;

        return new AircraftState(
            DateTimeOffset.UtcNow, position.LatitudeDegrees, position.LongitudeDegrees, 3000, onGround ? 0 : 500, 0, 150, onGround, null, null, 1200, "PR-ABC", "C172");
    }

    private static FlightContext Taxiing(int node, FlightPhase phase = FlightPhase.HandedToTower) =>
        new(Own, phase, "SBXX", null, null, null, "15", AtNode(node));

    [Fact]
    public void Held_short_of_the_runway_to_cross_the_torre_clears_the_crossing_first()
    {
        var decision = new AtcEngine().Handle(new ReportHoldingShort(Own, null), Taxiing(2), OnTower);

        decision.Instruction.ShouldBe(new CrossRunway("11", null, LineUpRunway: "15"));
        decision.NextContext.PendingReadback.ShouldBe(decision.Instruction);
        decision.NextContext.LastInstruction.ShouldBe(decision.Instruction);
        decision.NextContext.Phase.ShouldBe(FlightPhase.LineUpCleared);
        decision.NextContext.AssignedRunway.ShouldBe("15");
    }

    [Fact]
    public void Asking_for_the_departure_there_gets_the_crossing_too()
    {
        new AtcEngine().Handle(new RequestDeparture(Own, null), Taxiing(2), OnTower).Instruction.ShouldBe(new CrossRunway("11", null, LineUpRunway: "15"));
    }

    [Fact]
    public void Held_short_of_the_departure_runway_after_the_crossing_the_torre_clears_the_takeoff()
    {
        new AtcEngine().Handle(new ReportHoldingShort(Own, null), Taxiing(5), OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void On_the_far_side_of_the_crossed_runway_the_way_has_no_crossing_left()
    {
        new AtcEngine().Handle(new ReportHoldingShort(Own, null), Taxiing(4), OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void An_aircraft_that_is_not_at_the_holding_point_gets_what_it_always_got()
    {
        new AtcEngine().Handle(new ReportHoldingShort(Own, null), Taxiing(1), OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void Without_the_map_or_the_position_the_torre_clears_the_takeoff_as_before()
    {
        var engine = new AtcEngine();

        engine.Handle(new ReportHoldingShort(Own, null), Taxiing(2), OnTower with { Layout = null }).Instruction.ShouldBeOfType<TakeoffClearance>();
        engine.Handle(new ReportHoldingShort(Own, null), Taxiing(2) with { Aircraft = null }, OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
        engine.Handle(new ReportHoldingShort(Own, null), Taxiing(2) with { Aircraft = AtNode(2, onGround: false) }, OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void The_end_said_is_the_one_that_points_the_way_of_the_departure()
    {
        var onThe33 = AirportContext.Create(TestAirports.WithSolo(), "33") with { Layout = Toy };
        var tower = FrequencyResolver.Apply(onThe33, onThe33.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);
        var flight = Taxiing(2) with { AssignedRunway = "33" };

        // 33 points to 330.2: of 11/29 the 29 (290.5) is the nearer end.
        new AtcEngine().Handle(new ReportHoldingShort(Own, null), flight, tower).Instruction.ShouldBe(new CrossRunway("29", null, LineUpRunway: "33"));
    }

    [Fact]
    public void The_crossing_is_read_back_and_then_the_flight_goes_on_to_the_line_up()
    {
        var engine = new AtcEngine();
        var told = engine.Handle(new ReportHoldingShort(Own, null), Taxiing(2), OnTower).NextContext;
        var intent = new RuleBasedNlu().Interpret("cruzando pista um um, alinhando e mantendo, Papa Romeo Alfa Bravo Charlie", told);

        intent.ShouldBeOfType<Readback>();
        engine.Handle(intent, told, OnTower).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(new ReportHoldingShort(Own, null), told with { Aircraft = AtNode(5) }, OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void With_someone_ahead_in_the_line_the_crossing_goes_with_the_wait_at_the_holding_point()
    {
        var decision = new AtcEngine().Handle(new ReportHoldingShort(Own, null), Taxiing(2), OnTower with { AircraftAhead = true });

        decision.Instruction.ShouldBe(new CrossRunway("11", null, HoldAfter: true));
        decision.NextContext.Phase.ShouldBe(FlightPhase.HandedToTower);
        decision.NextContext.PendingReadback.ShouldBe(decision.Instruction);
    }

    [Theory]
    [InlineData("cruzando pista um um, alinhando e mantendo pista um cinco, Papa Romeo Alfa Bravo Charlie", true)]
    [InlineData("cruzo, alinho e mantenho, Papa Romeo Alfa Bravo Charlie", true)]
    [InlineData("cruzando pista um um, Papa Romeo Alfa Bravo Charlie", false)]
    [InlineData("alinho e mantenho, Papa Romeo Alfa Bravo Charlie", false)]
    public void The_crossing_with_the_line_up_is_read_back_with_both_things(string said, bool correct)
    {
        var told = new AtcEngine().Handle(new ReportHoldingShort(Own, null), Taxiing(2), OnTower).NextContext;
        var intent = new RuleBasedNlu().Interpret(said, told);

        (new AtcEngine().Handle(intent, told, OnTower).Instruction is NoResponse).ShouldBe(correct);
    }

    [Fact]
    public void The_crossing_of_a_departure_is_phrased_with_the_line_up_or_with_the_wait()
    {
        var phraseology = new PortuguesePhraseology();
        var context = new PhraseContext(Own, OnTower);

        phraseology.Speak(new CrossRunway("10R", null, LineUpRunway: "10L"), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, cruze a PISTA UNO ZERO DIREITA, alinhe e mantenha PISTA UNO ZERO ESQUERDA.");
        phraseology.Speak(new CrossRunway("10R", null, HoldAfter: true), context)!.Text
            .ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, cruze a PISTA UNO ZERO DIREITA e mantenha antes da barra de parada.");
    }

    [Theory]
    [InlineData(FlightPhase.Parked)]
    [InlineData(FlightPhase.LineUpCleared)]
    public void Before_the_taxi_and_once_lined_up_there_is_no_crossing_to_clear(FlightPhase phase)
    {
        new AtcEngine().Handle(new ReportHoldingShort(Own, null), Taxiing(2, phase), OnTower).Instruction.ShouldNotBeOfType<CrossRunway>();
    }
}
