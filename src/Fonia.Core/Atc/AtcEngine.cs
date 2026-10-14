using Fonia.Core.Airports;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;

namespace Fonia.Core.Atc;

/// <summary>
/// The Solo's state machine for the M2: startup, pushback and taxi to the holding point of the runway in use. The
/// controller always approves (no delays or sequencing yet) and follows what the airport really has: a Solo, or a
/// Torre doing its job, or only an AFIS that informs, or nobody. The pilot's callsign is not checked: the user is
/// the only pilot in the session, and the NLU reports a missing or foreign callsign as null.
/// </summary>
public sealed class AtcEngine : IAtcEngine
{
    public AtcDecision Handle(Intent intent, FlightContext flight, AirportContext airport)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(airport);

        return intent switch
        {
            // The radio is on a frequency of no unit of this airport: nobody hears, whatever was said.
            _ when airport.Unit == ServiceUnit.None && airport.Tuned is { } tuned => Say(new NoOneOnFrequency(tuned), flight with { AwaitingCallsign = null }),
            Unknown => Say(new SayAgainRequest(), flight),
            CallsignOnly called => Called(called, flight, airport),
            SayAgain => Repeat(flight),
            Readback readback => CheckReadback(readback, flight),

            // The pilot rolling for takeoff needs no answer, with or without the callsign.
            ReportTakingOff when airport.Unit == ServiceUnit.Tower && flight.Phase >= FlightPhase.TakeoffCleared
                => new AtcDecision(new NoResponse(), flight with { PendingReadback = null, AwaitingCallsign = null }),
            _ when intent.Callsign is null => new AtcDecision(new RequestCallsign(), flight with { AwaitingCallsign = intent }),
            _ when airport.Unit is ServiceUnit.None or ServiceUnit.Afis => Say(new NoControlService(airport.Unit), flight with { AwaitingCallsign = null }),

            // On the Torre's frequency the Torre does not do the Solo's job when the airport has a Solo: it says where to call.
            RequestStartup or RequestPushback or RequestTaxi when airport.Unit == ServiceUnit.Tower && airport.Airport.FrequencyOf(AirportFrequencyType.Ground) is { } ground
                => Say(new CallTheSolo(ground.Frequency), Released(flight)),

            // The departure belongs to the Torre; the Solo, asked for it, passes the flight on.
            ReportHoldingShort or RequestDeparture or ReportLinedUp or ReportTakingOff when airport.Unit == ServiceUnit.Tower
                => Departure(intent, Released(flight), airport),
            ReportHoldingShort or RequestDeparture or ReportLinedUp or ReportTakingOff => HoldingShort(Released(flight), airport),
            RequestStartup => Issue(new StartupApproved(), Released(flight), FlightPhase.StartupApproved),
            RequestPushback pushback => Issue(new PushbackApproved(pushback.WithStartup), Released(flight), pushback.WithStartup ? FlightPhase.StartupApproved : FlightPhase.PushbackApproved),
            RequestTaxi => Taxi(Released(flight), airport),
            _ => Say(new SayAgainRequest(), flight),
        };
    }

    // A request with the right callsign is what the pilot says now; one held for the callsign is dropped.
    private static FlightContext Released(FlightContext flight) => flight with { AwaitingCallsign = null };

    // The pilot said only the callsign. If a request was held for the callsign, now it goes ahead.
    private AtcDecision Called(CallsignOnly called, FlightContext flight, AirportContext airport)
    {
        if (called.Callsign is null)
        {
            return new AtcDecision(new RequestCallsign(), flight);
        }

        return flight.AwaitingCallsign is { } held
            ? Handle(held with { Callsign = flight.Callsign }, flight with { AwaitingCallsign = null }, airport)
            : Say(new GoAhead(), flight);
    }

    // The Torre's part of a departure (MCA 100-16, Art. 126): ready at the holding point, line up and wait; ready again or lined up,
    // cleared for takeoff. The runway is the one the Solo cleared the taxi to.
    private static AtcDecision Departure(Intent intent, FlightContext flight, AirportContext airport)
    {
        if (flight.Phase < FlightPhase.Taxiing || intent is ReportTakingOff)
        {
            return Say(new SayAgainRequest(), flight);
        }

        var runway = flight.AssignedRunway ?? airport.RunwayInUse;

        if (runway is null)
        {
            return Say(new NoRunwayAvailable(), flight);
        }

        // Lined up (said so, or was told to and is ready again): the takeoff. Otherwise: the line-up.
        if (intent is ReportLinedUp || flight.Phase >= FlightPhase.LineUpCleared)
        {
            var clearance = new TakeoffClearance(runway, airport.Wind);

            return new AtcDecision(
                clearance,
                flight with { Phase = FlightPhase.TakeoffCleared, PendingReadback = clearance, LastInstruction = clearance, AssignedRunway = runway });
        }

        var lineUp = new LineUpAndWait(runway);

        return new AtcDecision(
            lineUp,
            flight with { Phase = FlightPhase.LineUpCleared, PendingReadback = lineUp, LastInstruction = lineUp, AssignedRunway = runway });
    }

    // The pilot is at the holding point: the Solo passes the flight to the Torre. Reporting it before any taxi was cleared makes no sense.
    private static AtcDecision HoldingShort(FlightContext flight, AirportContext airport)
    {
        if (flight.Phase < FlightPhase.Taxiing)
        {
            return Say(new SayAgainRequest(), flight);
        }

        if (airport.Airport.FrequencyOf(AirportFrequencyType.Tower) is not { } tower)
        {
            return Say(new NoTowerAvailable(), flight);
        }

        var handoff = new HandoffToTower(tower.Frequency);

        // The readback is waiting only so that the NLU takes what the pilot says next as an answer: it is optional (see CheckReadback).
        return new AtcDecision(handoff, flight with { Phase = FlightPhase.HandedToTower, PendingReadback = handoff, LastInstruction = handoff });
    }

    private static AtcDecision Taxi(FlightContext flight, AirportContext airport)
    {
        if (airport.RunwayInUse is null)
        {
            return Say(new NoRunwayAvailable(), flight);
        }

        var clearance = new TaxiClearance(airport.RunwayInUse);

        // The taxi clearance is what the manual requires to be read back (Art. 12); startup and pushback are read back too, as in practice.
        return new AtcDecision(clearance, flight with { Phase = FlightPhase.Taxiing, PendingReadback = clearance, LastInstruction = clearance, AssignedRunway = airport.RunwayInUse });
    }

    private static AtcDecision Repeat(FlightContext flight)
    {
        return flight.LastInstruction is { } last
            ? new AtcDecision(last, flight)
            : Say(new SayAgainRequest(), flight);
    }

    private static AtcDecision CheckReadback(Readback readback, FlightContext flight)
    {
        if (flight.PendingReadback is not { } expected)
        {
            return new AtcDecision(new NoResponse(), flight);
        }

        // The manual does not list the frequency change among what must be read back (Art. 12): pilots do it anyway, and the
        // controller takes it in silence, whatever the words and with or without the callsign, instead of arguing about it.
        if (expected is HandoffToTower)
        {
            return new AtcDecision(new NoResponse(), flight with { PendingReadback = null });
        }

        var heard = SpokenTokens.Tokenize(readback.Text);

        // The callsign is part of the readback (Art. 12 lists the registration; the practice ends the readback with it).
        if (readback.Callsign is not null && IsCorrect(expected, heard))
        {
            return new AtcDecision(new NoResponse(), flight with { PendingReadback = null });
        }

        // Wrong or incomplete: "negativo" and the right version; the instruction is still waiting for its readback.
        return Say(new RequestReadbackCorrection(expected), flight);
    }

    // The taxi readback must have the runway; the startup and pushback ones must say what was approved.
    private static bool IsCorrect(AtcInstruction expected, IReadOnlyList<string> heard) => expected switch
    {
        TaxiClearance taxi => RuleBasedNlu.FindRunwayInReadback(heard) is { } runway && RunwayDesignator.AreSame(runway, taxi.Runway),
        StartupApproved => RuleBasedNlu.MentionsStartup(heard),
        PushbackApproved pushback => RuleBasedNlu.MentionsPushback(heard) && (!pushback.WithStartup || RuleBasedNlu.MentionsStartup(heard)),
        LineUpAndWait lineUp => RuleBasedNlu.MentionsLineUp(heard) && SaysRunway(heard, lineUp.Runway),
        TakeoffClearance takeoff => RuleBasedNlu.MentionsTakeoff(heard) && SaysRunway(heard, takeoff.Runway),
        _ => true,
    };

    private static bool SaysRunway(IReadOnlyList<string> heard, string runway) =>
        RuleBasedNlu.FindRunwayInReadback(heard) is { } said && RunwayDesignator.AreSame(said, runway);

    // Answers without moving the flight on. A "REPITA" is not remembered: if the pilot asks to say it again, what is
    // repeated is the last instruction.
    private static AtcDecision Say(AtcInstruction instruction, FlightContext flight) =>
        new(instruction, instruction is SayAgainRequest ? flight : flight with { LastInstruction = instruction });

    private static AtcDecision Issue(AtcInstruction instruction, FlightContext flight, FlightPhase phase) =>
        new(instruction, flight with { Phase = phase > flight.Phase ? phase : flight.Phase, PendingReadback = instruction, LastInstruction = instruction });
}
