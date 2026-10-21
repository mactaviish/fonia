using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// The controller sees the aircraft: close to a holding point or to the runway, it gives the next instruction as soon as it can, without the
// pilot saying "pronto" (the toy airport: taxiing out for 15, node 2 is the holding point of 11/29, node 5 the one of 15/33).
public class TowerInitiativeTests
{
    private const string Own = "PRABC";

    private static readonly AirportLayout Toy = TestLayouts.Toy();

    private static readonly AirportContext Solo = AirportContext.Create(TestAirports.WithSolo(), "15") with { Layout = Toy };

    private static readonly AirportContext OnTower = FrequencyResolver.Apply(Solo, Solo.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private static readonly AirportContext OnSolo = FrequencyResolver.Apply(Solo, Solo.Airport.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly NearRunway Near = new(Now, null);

    private static AircraftState AtNode(int id, bool onGround = true)
    {
        var position = Toy.Nodes.Single(n => n.Id == id).Position;

        return new AircraftState(Now, position.LatitudeDegrees, position.LongitudeDegrees, 3000, onGround ? 0 : 500, 5, 150, onGround, null, null, 1200, "PR-ABC", "C172");
    }

    private static FlightContext Flight(int node, FlightPhase phase = FlightPhase.HandedToTower, AtcInstruction? last = null) =>
        new(Own, phase, "SBXX", null, last, null, "15", AtNode(node));

    [Fact]
    public void At_the_holding_point_of_the_departure_runway_with_the_runway_free_the_torre_clears_the_takeoff_without_the_line_up()
    {
        var decision = new AtcEngine().Handle(Near, Flight(5), OnTower);

        decision.Instruction.ShouldBeOfType<TakeoffClearance>();
        decision.NextContext.Phase.ShouldBe(FlightPhase.TakeoffCleared);
    }

    [Fact]
    public void With_the_runway_busy_the_torre_lines_the_aircraft_up_and_clears_the_takeoff_when_the_runway_is_free()
    {
        var engine = new AtcEngine();
        var busy = OnTower with { RunwayBusy = true };
        var decision = engine.Handle(Near, Flight(5), busy);

        decision.Instruction.ShouldBe(new LineUpAndWait("15"));
        decision.NextContext.Phase.ShouldBe(FlightPhase.LineUpCleared);
        decision.NextContext.PendingReadback.ShouldBe(decision.Instruction);

        var readBack = decision.NextContext with { PendingReadback = null };

        engine.Handle(Near, readBack, busy).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(new TrafficCleared(Now), readBack, OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void The_same_event_again_says_nothing_more()
    {
        var engine = new AtcEngine();
        var told = engine.Handle(Near, Flight(5), OnTower with { RunwayBusy = true }).NextContext;

        engine.Handle(Near, told, OnTower with { RunwayBusy = true }).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void The_takeoff_waits_for_the_readback_of_the_line_up_unless_the_aircraft_is_already_on_the_runway()
    {
        var engine = new AtcEngine();
        var lineUp = new LineUpAndWait("15");
        var waiting = Flight(5, FlightPhase.LineUpCleared, lineUp) with { PendingReadback = lineUp };

        engine.Handle(Near, waiting, OnTower).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(Near, waiting with { Aircraft = AtNode(8) }, OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void At_the_holding_point_of_a_runway_to_cross_the_torre_clears_the_crossing_with_the_line_up_once()
    {
        var engine = new AtcEngine();
        var decision = engine.Handle(Near, Flight(2), OnTower);

        decision.Instruction.ShouldBe(new CrossRunway("11", null, LineUpRunway: "15"));
        engine.Handle(Near, Flight(2, FlightPhase.HandedToTower, decision.Instruction), OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Told_to_line_up_the_torre_clears_the_takeoff_when_the_aircraft_gets_to_the_runway_lined_up_or_not()
    {
        var engine = new AtcEngine();
        var lineUp = new LineUpAndWait("15");

        foreach (var node in new[] { 5, 8 })
        {
            var decision = engine.Handle(Near, Flight(node, FlightPhase.LineUpCleared, lineUp), OnTower);

            decision.Instruction.ShouldBeOfType<TakeoffClearance>();
            decision.NextContext.Phase.ShouldBe(FlightPhase.TakeoffCleared);
        }
    }

    [Fact]
    public void Told_to_cross_and_line_up_the_takeoff_comes_when_the_aircraft_gets_to_the_departure_runway()
    {
        var crossing = new CrossRunway("11", null, LineUpRunway: "15");
        var engine = new AtcEngine();

        engine.Handle(Near, Flight(4, FlightPhase.LineUpCleared, crossing), OnTower).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(Near, Flight(5, FlightPhase.LineUpCleared, crossing), OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void With_someone_ahead_nothing_is_said_until_the_line_is_clear_and_then_it_is_said_by_itself()
    {
        var engine = new AtcEngine();
        var ahead = OnTower with { AircraftAhead = true };

        engine.Handle(Near, Flight(5), ahead).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(Near, Flight(5, FlightPhase.LineUpCleared, new LineUpAndWait("15")), ahead).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(new TrafficCleared(Now), Flight(5), OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
        engine.Handle(new TrafficCleared(Now), Flight(5, FlightPhase.LineUpCleared, new LineUpAndWait("15")), OnTower).Instruction.ShouldBeOfType<TakeoffClearance>();
    }

    [Fact]
    public void When_the_line_clears_the_aircraft_held_at_the_crossing_gets_the_crossing_with_the_line_up()
    {
        var engine = new AtcEngine();
        var held = engine.Handle(Near, Flight(2), OnTower with { AircraftAhead = true });
        var flight = Flight(2, FlightPhase.HandedToTower, held.Instruction);

        held.Instruction.ShouldBe(new CrossRunway("11", null, HoldAfter: true));
        engine.Handle(new TrafficCleared(Now), flight, OnTower).Instruction.ShouldBe(new CrossRunway("11", null, LineUpRunway: "15"));
    }

    [Theory]
    [InlineData(FlightPhase.HandedToTower)]
    [InlineData(FlightPhase.LineUpCleared)]
    public void A_pilot_who_calls_while_someone_is_ahead_is_told_to_wait(FlightPhase phase)
    {
        var decision = new AtcEngine().Handle(new ReportHoldingShort(Own, null), Flight(5, phase), OnTower with { AircraftAhead = true });

        decision.Instruction.ShouldBeOfType<Standby>();
        new PortuguesePhraseology().Speak(decision.Instruction, new PhraseContext(Own, OnTower))!.Text.ShouldBe("PAPA ROMEO ALFA BRAVO CHARLIE, aguarde.");
    }

    [Fact]
    public void The_solo_passes_the_aircraft_to_the_torre_when_it_gets_to_a_holding_point_and_not_before()
    {
        var engine = new AtcEngine();

        engine.Handle(Near, Flight(5, FlightPhase.Taxiing), OnSolo).Instruction.ShouldBeOfType<HandoffToTower>();
        engine.Handle(Near, Flight(2, FlightPhase.Taxiing), OnSolo).Instruction.ShouldBeOfType<HandoffToTower>();
        engine.Handle(Near, Flight(1, FlightPhase.Taxiing), OnSolo).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(Near, Flight(5), OnSolo).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Far_from_the_holding_points_and_the_runway_the_controller_says_nothing()
    {
        new AtcEngine().Handle(Near, Flight(1, FlightPhase.Taxiing), OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void Without_the_map_the_position_or_the_ground_the_controller_says_nothing()
    {
        var engine = new AtcEngine();

        engine.Handle(Near, Flight(5), OnTower with { Layout = null }).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(Near, Flight(5) with { Aircraft = null }, OnTower).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(Near, Flight(5) with { Aircraft = AtNode(5, onGround: false) }, OnTower).Instruction.ShouldBeOfType<NoResponse>();
        engine.Handle(new NearRunway(Now, Toy.Nodes.Single(n => n.Id == 5).Position), Flight(5) with { Aircraft = null }, OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Theory]
    [InlineData(FlightPhase.Parked)]
    [InlineData(FlightPhase.TakeoffCleared)]
    [InlineData(FlightPhase.Airborne)]
    [InlineData(FlightPhase.Landed)]
    public void Outside_the_taxi_out_the_controller_says_nothing(FlightPhase phase)
    {
        new AtcEngine().Handle(Near, Flight(5, phase), OnTower).Instruction.ShouldBeOfType<NoResponse>();
    }
}
