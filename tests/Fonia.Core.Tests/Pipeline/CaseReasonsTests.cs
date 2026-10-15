using Fonia.Core.Atc;
using Fonia.Core.Nlu;
using Fonia.Core.Pipeline;

namespace Fonia.Core.Tests.Pipeline;

// Which exchanges are worth keeping to improve the app: the ones where the controller could not follow the pilot.
public class CaseReasonsTests
{
    private const string Own = "PRABC";

    private static readonly FlightContext Parked = new(Own, FlightPhase.Parked, "SBCT", null);

    private static readonly FlightContext Taxiing = new(Own, FlightPhase.Taxiing, "SBCT", new TaxiClearance("15"));

    [Fact]
    public void What_was_not_understood_is_a_case()
    {
        CaseReasons.Of(Parked, new Unknown("blá"), new SayAgainRequest()).ShouldBe([CaseReasons.NotUnderstood]);
    }

    [Fact]
    public void A_readback_the_controller_corrected_is_a_case_because_the_recognizer_may_be_the_one_that_is_wrong()
    {
        CaseReasons.Of(Taxiing, new Readback(Own, "pista dois nove"), new RequestReadbackCorrection(new TaxiClearance("15"))).ShouldBe([CaseReasons.Corrected]);
    }

    [Fact]
    public void A_missing_callsign_is_a_case()
    {
        CaseReasons.Of(Parked, new RequestStartup(null, null), new RequestCallsign()).ShouldBe([CaseReasons.NoCallsign]);
    }

    [Fact]
    public void Only_the_callsign_while_a_readback_waits_is_a_case_but_not_when_nothing_waits()
    {
        CaseReasons.Of(Taxiing, new CallsignOnly(Own), new GoAhead()).ShouldBe([CaseReasons.GoAheadInsteadOfReadback]);
        CaseReasons.Of(Parked, new CallsignOnly(Own), new GoAhead()).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_callsign_after_a_notice_that_needs_no_readback_is_the_acknowledgement_and_not_a_case()
    {
        var notice = new FlightContext(Own, FlightPhase.Airborne, "SBCT", new AirborneNotice(26));

        CaseReasons.Of(notice, new CallsignOnly(Own), new NoResponse()).ShouldBeEmpty();
    }

    [Fact]
    public void An_exchange_that_went_well_is_not_a_case()
    {
        CaseReasons.Of(Parked, new RequestStartup(Own, null), new StartupApproved()).ShouldBeEmpty();
        CaseReasons.Of(Taxiing, new Readback(Own, "pista uno cinco"), new NoResponse()).ShouldBeEmpty();
    }

    [Fact]
    public void A_pilot_who_asks_to_repeat_gets_the_last_instruction_and_that_is_not_a_case()
    {
        CaseReasons.Of(Taxiing, new SayAgain(Own), new TaxiClearance("15")).ShouldBeEmpty();
    }

    [Fact]
    public void The_reasons_are_names_for_the_file_and_do_not_repeat()
    {
        CaseReasons.All.ShouldBe(CaseReasons.All.Distinct().ToList());
        CaseReasons.All.ShouldAllBe(r => r.Length > 0 && r.All(c => char.IsAsciiLetter(c) || c == '-'));
    }
}
