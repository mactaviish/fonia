using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Sim;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

// The Controle of an airport (its approach) is not played by the app yet: it answers the call and sends the rest to the Torre.
public class ApproachUnitTests
{
    private const string Own = "PRABC";

    private readonly AtcEngine _engine = new();

    private static readonly Airport WithApproach = TestAirports.Airport(
        "SBXX",
        [TestAirports.Runway("11", "29", 9000)],
        AirportFrequencyType.Ground,
        AirportFrequencyType.Tower,
        AirportFrequencyType.Approach);

    private static readonly AirportContext OnApproach =
        FrequencyResolver.Apply(AirportContext.Create(WithApproach), WithApproach.FrequencyOf(AirportFrequencyType.Approach)!.Frequency);

    private static readonly FlightContext Airborne = new(Own, FlightPhase.Airborne, "SBXX", null);

    private static Frequency Tower => WithApproach.FrequencyOf(AirportFrequencyType.Tower)!.Frequency;

    public static TheoryData<Intent> ThingsOfTheTorreOrTheSolo() =>
        new()
        {
            new RequestStartup(Own, null),
            new RequestPushback(Own, false, null),
            new RequestTaxi(Own, null),
            new ReportHoldingShort(Own, "11"),
            new RequestDeparture(Own, null),
            new RequestLanding(Own),
            new ReportDownwind(Own),
            new ReportFinal(Own),
        };

    [Fact]
    public void The_callsign_alone_on_the_approach_is_invited_to_go_ahead()
    {
        _engine.Handle(new CallsignOnly(Own), Airborne, OnApproach).Instruction.ShouldBe(new GoAhead());
    }

    [Theory]
    [MemberData(nameof(ThingsOfTheTorreOrTheSolo))]
    public void What_belongs_to_the_torre_is_sent_to_the_torre_with_its_frequency(Intent intent)
    {
        var decision = _engine.Handle(intent, Airborne, OnApproach);

        decision.Instruction.ShouldBe(new CallTheTower(Tower));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Airborne);
    }

    [Fact]
    public void An_airport_without_a_torre_has_nobody_to_send_the_flight_to()
    {
        var approachOnly = TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], AirportFrequencyType.Approach);
        var onApproach = FrequencyResolver.Apply(AirportContext.Create(approachOnly), approachOnly.FrequencyOf(AirportFrequencyType.Approach)!.Frequency);

        _engine.Handle(new RequestLanding(Own), Airborne, onApproach).Instruction.ShouldBe(new NoTowerAvailable());
    }

    [Fact]
    public void Without_the_callsign_the_pilot_is_asked_for_it_as_on_any_other_unit()
    {
        _engine.Handle(new RequestLanding(null), Airborne, OnApproach).Instruction.ShouldBe(new RequestCallsign());
    }

    [Fact]
    public void The_conditions_of_the_aerodrome_are_still_given()
    {
        _engine.Handle(new RequestInformation(Own), Airborne, OnApproach).Instruction.ShouldBeOfType<DepartureInformation>();
    }

    [Fact]
    public void Asking_to_say_it_again_repeats_the_last_instruction()
    {
        var sent = _engine.Handle(new RequestLanding(Own), Airborne, OnApproach).NextContext;

        _engine.Handle(new SayAgain(Own), sent, OnApproach).Instruction.ShouldBe(new CallTheTower(Tower));
    }

    [Fact]
    public void The_torre_does_not_notice_the_takeoff_while_the_radio_is_on_the_approach()
    {
        var cleared = Airborne with { Phase = FlightPhase.TakeoffCleared };

        var decision = _engine.Handle(new TookOff(DateTimeOffset.UnixEpoch), cleared, OnApproach);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.ShouldBe(cleared);
    }
}
