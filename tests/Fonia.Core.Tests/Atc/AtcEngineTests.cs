using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Atc;

public class AtcEngineTests
{
    private const string Own = "PRABC";

    private static readonly AirportContext Solo = AirportContext.Create(TestAirports.WithSolo());

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBXX", null);

    private readonly AtcEngine _engine = new();

    private static AirportContext WithUnit(params AirportFrequencyType[] frequencies) =>
        AirportContext.Create(TestAirports.Airport("SBXX", [TestAirports.Runway("11", "29", 9000)], frequencies));

    [Fact]
    public void Startup_is_approved()
    {
        var decision = _engine.Handle(new RequestStartup(Own, "box 3"), Parked, Solo);

        decision.Instruction.ShouldBe(new StartupApproved());
        decision.NextContext.Phase.ShouldBe(FlightPhase.StartupApproved);
        decision.NextContext.PendingReadback.ShouldBe(new StartupApproved());
    }

    [Theory]
    [InlineData(false, FlightPhase.PushbackApproved)]
    [InlineData(true, FlightPhase.StartupApproved)]
    public void Pushback_is_approved_and_so_is_the_startup_asked_with_it(bool withStartup, FlightPhase phase)
    {
        var decision = _engine.Handle(new RequestPushback(Own, withStartup, null), Parked, Solo);

        decision.Instruction.ShouldBe(new PushbackApproved(withStartup));
        decision.NextContext.Phase.ShouldBe(phase);
    }

    [Fact]
    public void A_request_never_takes_the_flight_back_to_an_earlier_phase()
    {
        var taxiing = Parked with { Phase = FlightPhase.Taxiing };

        _engine.Handle(new RequestStartup(Own, null), taxiing, Solo).NextContext.Phase.ShouldBe(FlightPhase.Taxiing);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("33")]
    public void Taxi_goes_to_the_runway_in_use_whatever_the_pilot_asked(string? asked)
    {
        var decision = _engine.Handle(new RequestTaxi(Own, asked), Parked, Solo);

        decision.Instruction.ShouldBe(new TaxiClearance("11"));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Taxiing);
        decision.NextContext.PendingReadback.ShouldBe(new TaxiClearance("11"));
    }

    [Fact]
    public void Taxi_can_be_asked_without_a_startup_first()
    {
        _engine.Handle(new RequestTaxi(Own, null), Parked, Solo).Instruction.ShouldBeOfType<TaxiClearance>();
    }

    [Fact]
    public void An_airport_without_a_runway_cannot_clear_a_taxi()
    {
        var noRunway = AirportContext.Create(TestAirports.Airport("SBXX", [], AirportFrequencyType.Ground));

        var decision = _engine.Handle(new RequestTaxi(Own, null), Parked, noRunway);

        decision.Instruction.ShouldBe(new NoRunwayAvailable());
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Fact]
    public void A_tower_airport_without_a_solo_handles_the_same_requests()
    {
        var tower = WithUnit(AirportFrequencyType.Tower);

        _engine.Handle(new RequestStartup(Own, null), Parked, tower).Instruction.ShouldBe(new StartupApproved());
        _engine.Handle(new RequestTaxi(Own, null), Parked, tower).Instruction.ShouldBe(new TaxiClearance("11"));
    }

    [Theory]
    [InlineData(AirportFrequencyType.Afis, ServiceUnit.Afis)]
    [InlineData(AirportFrequencyType.Atis, ServiceUnit.None)]
    public void Where_nobody_controls_nothing_is_cleared(AirportFrequencyType frequency, ServiceUnit unit)
    {
        var airport = WithUnit(frequency);

        foreach (Intent request in new Intent[] { new RequestStartup(Own, null), new RequestPushback(Own, true, null), new RequestTaxi(Own, null) })
        {
            var decision = _engine.Handle(request, Parked, airport);

            decision.Instruction.ShouldBe(new NoControlService(unit));
            decision.NextContext.Phase.ShouldBe(FlightPhase.Parked);
            decision.NextContext.PendingReadback.ShouldBeNull();
        }
    }

    [Fact]
    public void What_was_not_understood_is_answered_with_a_request_to_say_it_again_and_changes_nothing()
    {
        var decision = _engine.Handle(new Unknown("blá"), Parked, Solo);

        decision.Instruction.ShouldBe(new SayAgainRequest());
        decision.NextContext.ShouldBe(Parked);
    }

    [Fact]
    public void A_correct_taxi_readback_gets_no_answer_and_clears_the_pending_instruction()
    {
        var cleared = _engine.Handle(new RequestTaxi(Own, null), Parked, Solo).NextContext;

        var decision = _engine.Handle(new Readback(Own, "autorizado taxi pista um um"), cleared, Solo);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.PendingReadback.ShouldBeNull();
        decision.NextContext.Phase.ShouldBe(FlightPhase.Taxiing);
    }

    [Theory]
    [InlineData("autorizado taxi pista um cinco")]
    [InlineData("autorizado taxi pista um um esquerda")]
    [InlineData("ciente")]
    [InlineData("autorizado taxi")]
    public void A_wrong_or_incomplete_taxi_readback_is_corrected_and_still_pending(string said)
    {
        var cleared = _engine.Handle(new RequestTaxi(Own, null), Parked, Solo).NextContext;

        var decision = _engine.Handle(new Readback(Own, said), cleared, Solo);

        decision.Instruction.ShouldBe(new RequestReadbackCorrection(new TaxiClearance("11")));
        decision.NextContext.PendingReadback.ShouldBe(new TaxiClearance("11"));
    }

    [Theory]
    [InlineData("acionamento aprovado, chamo pronto para o taxi")]
    [InlineData("aprovado acionar")]
    public void The_startup_readback_must_say_what_was_approved(string said)
    {
        var approved = _engine.Handle(new RequestStartup(Own, null), Parked, Solo).NextContext;

        var decision = _engine.Handle(new Readback(Own, said), approved, Solo);

        decision.Instruction.ShouldBe(new NoResponse());
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Theory]
    [InlineData("ciente")]
    [InlineData("pushback aprovado")]
    public void A_startup_readback_that_does_not_say_it_is_corrected(string said)
    {
        var approved = _engine.Handle(new RequestStartup(Own, null), Parked, Solo).NextContext;

        var decision = _engine.Handle(new Readback(Own, said), approved, Solo);

        decision.Instruction.ShouldBe(new RequestReadbackCorrection(new StartupApproved()));
        decision.NextContext.PendingReadback.ShouldBe(new StartupApproved());
    }

    [Theory]
    [InlineData("pushback e acionamento autorizados", false)]
    [InlineData("autorizado pushback", true)]
    [InlineData("autorizado acionamento", true)]
    [InlineData("ciente", true)]
    public void A_pushback_with_startup_must_be_read_back_with_both(string said, bool corrected)
    {
        var approved = _engine.Handle(new RequestPushback(Own, true, null), Parked, Solo).NextContext;

        var decision = _engine.Handle(new Readback(Own, said), approved, Solo);

        (decision.Instruction is RequestReadbackCorrection).ShouldBe(corrected);
    }

    [Fact]
    public void A_pushback_alone_is_read_back_with_pushback()
    {
        var approved = _engine.Handle(new RequestPushback(Own, false, null), Parked, Solo).NextContext;

        approved.PendingReadback.ShouldBe(new PushbackApproved(false));
        _engine.Handle(new Readback(Own, "pushback aprovado"), approved, Solo).Instruction.ShouldBe(new NoResponse());
    }

    [Fact]
    public void A_readback_with_nothing_pending_gets_no_answer()
    {
        _engine.Handle(new Readback(Own, "ciente"), Parked, Solo).Instruction.ShouldBe(new NoResponse());
    }

    [Fact]
    public void Asking_to_say_it_again_repeats_the_last_instruction()
    {
        var approved = _engine.Handle(new RequestStartup(Own, null), Parked, Solo).NextContext;

        var decision = _engine.Handle(new SayAgain(Own), approved, Solo);

        decision.Instruction.ShouldBe(new StartupApproved());
    }

    [Fact]
    public void Asking_to_say_it_again_after_not_being_understood_still_repeats_the_instruction_not_the_request()
    {
        var approved = _engine.Handle(new RequestStartup(Own, null), Parked, Solo).NextContext;
        var misunderstood = _engine.Handle(new Unknown("blá"), approved, Solo).NextContext;

        _engine.Handle(new SayAgain(Own), misunderstood, Solo).Instruction.ShouldBe(new StartupApproved());
    }

    [Fact]
    public void Asking_to_say_it_again_before_anything_was_said_asks_the_pilot_to_say_it()
    {
        _engine.Handle(new SayAgain(Own), Parked, Solo).Instruction.ShouldBe(new SayAgainRequest());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_request_without_the_aircrafts_callsign_is_not_answered_and_the_callsign_is_asked(int which)
    {
        Intent request = which switch
        {
            0 => new RequestStartup(null, "box 3"),
            1 => new RequestPushback(null, true, null),
            _ => new RequestTaxi(null, null),
        };

        var decision = _engine.Handle(request, Parked, Solo);

        decision.Instruction.ShouldBe(new RequestCallsign());
        decision.NextContext.Phase.ShouldBe(FlightPhase.Parked);
        decision.NextContext.PendingReadback.ShouldBeNull();
        decision.NextContext.AwaitingCallsign.ShouldBe(request);
    }

    [Fact]
    public void The_callsign_is_asked_again_until_it_is_right_and_then_the_held_request_goes_ahead()
    {
        var asked = _engine.Handle(new RequestStartup(null, null), Parked, Solo).NextContext;
        var askedAgain = _engine.Handle(new RequestStartup(null, null), asked, Solo);

        askedAgain.Instruction.ShouldBe(new RequestCallsign());

        var decision = _engine.Handle(new CallsignOnly(Own), askedAgain.NextContext, Solo);

        decision.Instruction.ShouldBe(new StartupApproved());
        decision.NextContext.Phase.ShouldBe(FlightPhase.StartupApproved);
        decision.NextContext.AwaitingCallsign.ShouldBeNull();
    }

    [Fact]
    public void A_new_request_with_the_right_callsign_replaces_the_held_one()
    {
        var asked = _engine.Handle(new RequestStartup(null, null), Parked, Solo).NextContext;

        var decision = _engine.Handle(new RequestTaxi(Own, null), asked, Solo);

        decision.Instruction.ShouldBeOfType<TaxiClearance>();
        decision.NextContext.AwaitingCallsign.ShouldBeNull();
    }

    [Fact]
    public void A_request_held_for_the_callsign_is_not_forgotten_by_a_misunderstood_phrase()
    {
        var asked = _engine.Handle(new RequestTaxi(null, null), Parked, Solo).NextContext;

        var unknown = _engine.Handle(new Unknown("blá"), asked, Solo);

        unknown.Instruction.ShouldBe(new SayAgainRequest());
        _engine.Handle(new CallsignOnly(Own), unknown.NextContext, Solo).Instruction.ShouldBeOfType<TaxiClearance>();
    }

    [Fact]
    public void Calling_the_controller_with_nothing_held_invites_the_pilot_to_go_ahead()
    {
        _engine.Handle(new CallsignOnly(Own), Parked, Solo).Instruction.ShouldBe(new GoAhead());
    }

    [Fact]
    public void A_readback_is_accepted_without_the_callsign()
    {
        var approved = _engine.Handle(new RequestStartup(Own, null), Parked, Solo).NextContext;

        _engine.Handle(new Readback(null, "acionamento aprovado"), approved, Solo).Instruction.ShouldBe(new NoResponse());
    }

    [Fact]
    public void A_held_request_at_an_airport_without_service_is_answered_when_the_callsign_comes()
    {
        var afis = WithUnit(AirportFrequencyType.Afis);
        var asked = _engine.Handle(new RequestStartup(null, null), Parked, afis).NextContext;

        _engine.Handle(new CallsignOnly(Own), asked, afis).Instruction.ShouldBe(new NoControlService(ServiceUnit.Afis));
    }

    [Fact]
    public void A_full_flow_from_the_gate_to_the_taxi_readback()
    {
        var nlu = new RuleBasedNlu();
        var flight = Parked;

        AtcDecision Say(string said)
        {
            var decision = _engine.Handle(nlu.Interpret(said, flight), flight, Solo);
            flight = decision.NextContext;
            return decision;
        }

        Say("Solo, box 3, solicita pushback e acionamento").Instruction.ShouldBe(new RequestCallsign());
        Say("PRABC").Instruction.ShouldBe(new PushbackApproved(true));
        Say("Solo, PRABC, box 3, solicita pushback e acionamento").Instruction.ShouldBe(new PushbackApproved(true));
        Say("Autorizado pushback e acionamento, PRABC").Instruction.ShouldBe(new NoResponse());
        Say("Solo, PRABC, pronto para o táxi").Instruction.ShouldBe(new TaxiClearance("11"));
        Say("Táxi pista um um, PRABC").Instruction.ShouldBe(new NoResponse());
        flight.PendingReadback.ShouldBeNull();
        flight.Phase.ShouldBe(FlightPhase.Taxiing);
    }
}
