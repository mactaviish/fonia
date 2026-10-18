using Fonia.Core.Airports;
using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Tests.TestSupport;

namespace Fonia.Core.Tests.Nlu;

// The two "repita" of the flight of 2026-10-18 (SBCT, PR-FRA), with the words the recognizer wrote: both are cases kept by the app itself.
public class FlightOfOctober18Tests
{
    private const string Own = "PRFRA";

    private static readonly AirportContext SoloAndTower = AirportContext.Create(TestAirports.WithSolo());

    private static readonly AirportContext OnTower =
        FrequencyResolver.Apply(SoloAndTower, SoloAndTower.Airport.FrequencyOf(AirportFrequencyType.Tower)!.Frequency);

    private readonly RuleBasedNlu _nlu = new();
    private readonly AtcEngine _engine = new();

    private AtcDecision Say(string said, FlightContext flight) => _engine.Handle(_nlu.Interpret(said, flight), flight, OnTower);

    private static FlightContext Flight(FlightPhase phase, AtcInstruction? pending) => new(Own, phase, "SBCT", pending, pending, null, "15");

    // 1. After "decolado aos UNO MEIA" the pilot acknowledged and the recognizer wrote "Foxrochome Alf Sint.": it was "repita".
    [Theory]
    [InlineData("Foxrochome Alf Sint.")]
    [InlineData("Cientio Foxtroot Romeo Alfa.")]
    [InlineData("blá blá")]
    public void An_acknowledgement_that_was_not_understood_after_a_notice_is_still_taken_in_silence(string said)
    {
        var decision = Say(said, Flight(FlightPhase.Airborne, new AirborneNotice(16)));

        decision.Instruction.ShouldBeOfType<NoResponse>();
        decision.NextContext.PendingReadback.ShouldBeNull();
    }

    [Theory]
    [InlineData(typeof(LandedNotice))]
    [InlineData(typeof(ReportBase))]
    [InlineData(typeof(ReportOnFinal))]
    [InlineData(typeof(HandoffToTower))]
    public void The_same_goes_for_the_other_notices_that_need_no_readback(Type notice)
    {
        AtcInstruction pending = notice == typeof(LandedNotice) ? new LandedNotice(22, null)
            : notice == typeof(HandoffToTower) ? new HandoffToTower(new Fonia.Core.Sim.Frequency(118550))
            : notice == typeof(ReportBase) ? new ReportBase()
            : new ReportOnFinal();

        Say("Foxrochome Alf Sint.", Flight(FlightPhase.Airborne, pending)).Instruction.ShouldBeOfType<NoResponse>();
    }

    [Fact]
    public void An_instruction_that_needs_a_readback_is_not_acknowledged_by_a_mumble()
    {
        Say("Foxrochome Alf Sint.", Flight(FlightPhase.Taxiing, new TaxiClearance("15"))).Instruction.ShouldBeOfType<SayAgainRequest>();
    }

    [Fact]
    public void With_nothing_to_acknowledge_a_mumble_is_still_not_understood()
    {
        Say("Foxrochome Alf Sint.", Flight(FlightPhase.Airborne, null)).Instruction.ShouldBeOfType<SayAgainRequest>();
    }

    [Fact]
    public void A_request_after_a_notice_is_still_a_request_and_not_an_acknowledgement()
    {
        Say("Torre Curitiba, Papa Romeo Foxtrot Romeo Alfa, solicito pouso", Flight(FlightPhase.Airborne, new AirborneNotice(16)))
            .Instruction.ShouldBeOfType<CircuitClearance>();
    }

    // 2. After landing, "pista livre, Foxtrot Romeo Alfa" was written "Pista de Vrio Foxtroot Romeo Alfa.": it was "repita".
    [Theory]
    [InlineData("Pista de Vrio Foxtroot Romeo Alfa.")]
    [InlineData("Torre Curitiba, Papa Romeo Foxtrot Romeo Alfa, pista")]
    public void After_landing_the_word_pista_with_the_callsign_and_no_number_is_the_runway_vacated(string said)
    {
        Say(said, Flight(FlightPhase.Landed, null)).Instruction.ShouldBeOfType<HandoffToGround>();
    }

    [Fact]
    public void After_landing_a_runway_with_a_number_is_not_the_runway_vacated()
    {
        Say("Pista uno cinco, Foxtrot Romeo Alfa", Flight(FlightPhase.Landed, null)).Instruction.ShouldNotBeOfType<HandoffToGround>();
    }

    [Fact]
    public void Outside_the_landed_phase_the_word_pista_alone_is_nothing()
    {
        Say("Pista de Vrio Foxtroot Romeo Alfa.", Flight(FlightPhase.Taxiing, null)).Instruction.ShouldBeOfType<SayAgainRequest>();
    }
}
