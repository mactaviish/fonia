using Fonia.Core.Atc;

namespace Fonia.Core.Tests.Atc;

public class AtcDecisionTests
{
    private static FlightContext Parked() => new("PRABC", FlightPhase.Parked, "SBCT", PendingReadback: null);

    [Fact]
    public void Decision_carries_instruction_and_next_context()
    {
        var instruction = new TaxiClearance("15");
        var next = Parked() with { Phase = FlightPhase.Taxiing, PendingReadback = instruction };

        var decision = new AtcDecision(instruction, next);

        decision.Instruction.ShouldBe(new TaxiClearance("15"));
        decision.NextContext.Phase.ShouldBe(FlightPhase.Taxiing);
        decision.NextContext.PendingReadback.ShouldBe(instruction);
    }

    [Fact]
    public void Decisions_are_equal_by_value()
    {
        var a = new AtcDecision(new TaxiClearance("15"), Parked());
        var b = new AtcDecision(new TaxiClearance("15"), Parked());

        a.ShouldBe(b);
    }

    [Fact]
    public void Readback_correction_wraps_the_expected_instruction()
    {
        AtcInstruction correction = new RequestReadbackCorrection(new TaxiClearance("15"));

        correction.ShouldBe(new RequestReadbackCorrection(new TaxiClearance("15")));
        correction.ShouldNotBe(new RequestReadbackCorrection(new TaxiClearance("33")));
    }

    [Fact]
    public void New_flight_context_has_no_pending_readback()
    {
        Parked().PendingReadback.ShouldBeNull();
    }
}
