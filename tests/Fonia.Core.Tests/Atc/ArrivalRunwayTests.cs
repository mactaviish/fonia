using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// An airport with parallel runways of different uses (Guarulhos: with the 10 in use, lands on the 10R and takes off from the 10L; with the 28,
// lands on the 28L and takes off from the 28R). The wind or the user chooses the direction; a rule says which runway does what.
public class ArrivalRunwayTests
{
    private const string Own = "PRABC";

    private static readonly RunwayUseRule[] Rules = [new RunwayUseRule("10R", "10L"), new RunwayUseRule("28L", "28R")];

    // 10L/28R is the longer, so the default runway in use is the 10L.
    private static readonly Airport Parallel = TestAirports.Airport(
        "SBXX",
        [TestAirports.Runway("10L", "28R", 12000), TestAirports.Runway("10R", "28L", 10000)],
        AirportFrequencyType.Ground,
        AirportFrequencyType.Tower);

    private static AirportContext Context(string? requested = null) => AirportContext.Create(Parallel, requested);

    private readonly AtcEngine _engine = new();

    [Fact]
    public void An_airport_without_a_rule_lands_and_takes_off_on_the_same_runway()
    {
        var context = Context();

        context.ArrivalRunway.ShouldBeNull();
        context.RunwayForArrival.ShouldBe(context.RunwayInUse);
        context.WithRunwayUse([], keepRunway: false).ShouldBe(context);
    }

    [Fact]
    public void When_the_departure_runway_is_the_one_in_use_the_rule_gives_the_landing_runway()
    {
        var context = Context("10L").WithRunwayUse(Rules, keepRunway: false);

        context.RunwayInUse.ShouldBe("10L");
        context.ArrivalRunway.ShouldBe("10R");
        context.RunwayForArrival.ShouldBe("10R");
    }

    [Fact]
    public void The_other_direction_has_its_own_rule()
    {
        var context = Context("28R").WithRunwayUse(Rules, keepRunway: false);

        context.RunwayInUse.ShouldBe("28R");
        context.RunwayForArrival.ShouldBe("28L");
    }

    [Fact]
    public void When_the_wind_chose_the_arrival_runway_the_departure_runway_is_the_one_in_use()
    {
        var context = Context("28L").WithRunwayUse(Rules, keepRunway: false);

        context.RunwayInUse.ShouldBe("28R");
        context.ArrivalRunway.ShouldBe("28L");
    }

    [Fact]
    public void A_runway_the_user_asked_for_as_the_arrival_is_kept_for_both()
    {
        var context = Context("10R").WithRunwayUse(Rules, keepRunway: true);

        context.RunwayInUse.ShouldBe("10R");
        context.ArrivalRunway.ShouldBeNull();
        context.RunwayForArrival.ShouldBe("10R");
    }

    [Fact]
    public void A_runway_the_user_asked_for_as_the_departure_still_gets_its_landing_runway()
    {
        var context = Context("10L").WithRunwayUse(Rules, keepRunway: true);

        context.ArrivalRunway.ShouldBe("10R");
    }

    [Fact]
    public void A_rule_with_a_runway_the_airport_does_not_have_is_ignored()
    {
        var single = AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("10L", "28R", 12000)], AirportFrequencyType.Tower));

        single.WithRunwayUse(Rules, keepRunway: false).ShouldBe(single);
    }

    [Fact]
    public void A_runway_that_is_closed_is_not_one_the_rule_can_use()
    {
        var airport = TestAirports.Airport("SBXX", [TestAirports.Runway("10L", "28R", 12000), TestAirports.Runway("10R", "28L", 10000, closed: true)], AirportFrequencyType.Tower);
        var context = AirportContext.Create(airport);

        context.WithRunwayUse(Rules, keepRunway: false).ShouldBe(context);
    }

    [Fact]
    public void An_airport_with_no_runway_in_use_is_left_alone()
    {
        var context = Context() with { RunwayInUse = null };

        context.WithRunwayUse(Rules, keepRunway: false).ShouldBe(context);
    }

    [Fact]
    public void The_user_can_ask_for_the_landing_runway()
    {
        var context = Context("10L").WithArrivalRunway("28l");

        context.ArrivalRunway.ShouldBe("28L");
        context.RunwayInUse.ShouldBe("10L");
    }

    [Fact]
    public void A_landing_runway_the_airport_does_not_have_is_refused()
    {
        Should.Throw<ArgumentException>(() => Context().WithArrivalRunway("09")).Message.ShouldContain("09");
    }

    // The Torre: the circuit and the landing on the arrival runway, the line-up and the takeoff on the departure runway.
    private static AirportContext OnTower() =>
        FrequencyResolver.Apply(Context("10L").WithRunwayUse(Rules, keepRunway: false), Parallel.FrequencyOf(AirportFrequencyType.Tower)!.Frequency) with { RunwayBusy = true };

    private static FlightContext Flying() => new(Own, FlightPhase.Airborne, "SBXX", null);

    [Fact]
    public void The_pilot_asking_to_land_is_cleared_to_the_circuit_on_the_arrival_runway()
    {
        var decision = _engine.Handle(new RequestLanding(Own), Flying(), OnTower());

        decision.Instruction.ShouldBe(new CircuitClearance("10R"));
        decision.NextContext.AssignedRunway.ShouldBe("10R");
    }

    [Fact]
    public void The_landing_clearance_after_the_circuit_is_on_the_runway_the_circuit_gave()
    {
        var circuit = _engine.Handle(new RequestLanding(Own), Flying(), OnTower()).NextContext;
        var onBase = circuit with { Phase = FlightPhase.OnBase };

        _engine.Handle(new ReportFinal(Own), onBase, OnTower()).Instruction.ShouldBe(new LandingClearance("10R"));
    }

    [Fact]
    public void A_pilot_who_reports_final_without_a_circuit_is_cleared_on_the_arrival_runway()
    {
        _engine.Handle(new ReportFinal(Own), Flying(), OnTower()).Instruction.ShouldBe(new LandingClearance("10R"));
    }

    [Fact]
    public void The_straight_in_approach_is_on_the_arrival_runway_too()
    {
        _engine.Handle(new RequestLanding(Own, StraightIn: true), Flying(), OnTower()).Instruction.ShouldBe(new StraightInClearance("10R"));
    }

    [Fact]
    public void The_taxi_and_the_departure_stay_on_the_departure_runway()
    {
        var parked = new FlightContext(Own, FlightPhase.Parked, "SBXX", null);
        var onSolo = FrequencyResolver.Apply(Context("10L").WithRunwayUse(Rules, keepRunway: false), Parallel.FrequencyOf(AirportFrequencyType.Ground)!.Frequency);

        var taxi = _engine.Handle(new RequestTaxi(Own, null), parked, onSolo);

        taxi.Instruction.ShouldBe(new TaxiClearance("10L"));

        var holding = taxi.NextContext with { Phase = FlightPhase.HandedToTower };
        _engine.Handle(new ReportHoldingShort(Own, "10L"), holding, OnTower()).Instruction.ShouldBe(new LineUpAndWait("10L"));
    }

    [Fact]
    public void The_conditions_of_the_aerodrome_given_to_a_departing_aircraft_name_the_departure_runway()
    {
        var decision = _engine.Handle(new RequestInformation(Own), new FlightContext(Own, FlightPhase.Parked, "SBXX", null), OnTower());

        decision.Instruction.ShouldBeOfType<DepartureInformation>().Runway.ShouldBe("10L");
    }
}
