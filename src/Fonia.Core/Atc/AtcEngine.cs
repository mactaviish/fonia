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
            Readback readback => CheckReadback(readback, flight, airport),

            // The pilot rolling for takeoff needs no answer, with or without the callsign.
            ReportTakingOff when airport.Unit == ServiceUnit.Tower && flight.Phase >= FlightPhase.TakeoffCleared
                => new AtcDecision(new NoResponse(), flight with { PendingReadback = null, AwaitingCallsign = null }),
            _ when intent.Callsign is null => new AtcDecision(new RequestCallsign(), flight with { AwaitingCallsign = intent }),
            // The conditions of the aerodrome are given by whoever answers, an AFIS included (it informs).
            RequestInformation when airport.Unit != ServiceUnit.None
                => Say(new DepartureInformation(airport.RunwayInUse, airport.Wind, airport.Qnh, airport.TemperatureCelsius, airport.VisibilityMeters), Released(flight)),
            _ when airport.Unit is ServiceUnit.None or ServiceUnit.Afis => Say(new NoControlService(airport.Unit), flight with { AwaitingCallsign = null }),

            // On the Torre's frequency the Torre does not do the Solo's job when the airport has a Solo: it says where to call.
            RequestStartup or RequestPushback or RequestTaxi when airport.Unit == ServiceUnit.Tower && airport.Airport.FrequencyOf(AirportFrequencyType.Ground) is { } ground
                => Say(new CallTheSolo(ground.Frequency), Released(flight)),

            // The runway is clear: the Torre passes the flight to the Solo; the Solo (or a Torre alone) takes it to the apron.
            ReportRunwayVacated when airport.Unit == ServiceUnit.Tower && airport.Airport.FrequencyOf(AirportFrequencyType.Ground) is { } ground
                => Vacated(Released(flight), new HandoffToGround(ground.Frequency), keepPhase: true),
            ReportRunwayVacated => Vacated(Released(flight), new TaxiToApron(), keepPhase: false),

            // The arrival belongs to the Torre; the Solo, asked for it, says where to call.
            RequestLanding or ReportDownwind or ReportBaseLeg or ReportFinal or ReportGoingAround when airport.Unit == ServiceUnit.Tower
                => Arrival(intent, Released(flight), airport),
            RequestLanding or ReportDownwind or ReportBaseLeg or ReportFinal or ReportGoingAround => CallTheTower(Released(flight), airport),

            // The departure belongs to the Torre; the Solo, asked for it, passes the flight on.
            ReportHoldingShort or RequestDeparture or ReportLinedUp or ReportTakingOff when airport.Unit == ServiceUnit.Tower
                => Departure(intent, Released(flight), airport),
            ReportHoldingShort or RequestDeparture or ReportLinedUp or ReportTakingOff => HoldingShort(Released(flight), airport),
            RequestStartup => Issue(new StartupApproved(), Released(flight), FlightPhase.StartupApproved),
            RequestPushback pushback => Issue(new PushbackApproved(pushback.WithStartup), Released(flight), pushback.WithStartup ? FlightPhase.StartupApproved : FlightPhase.PushbackApproved),
            // After the landing the taxi is to the apron, not to the holding point of a runway.
            RequestTaxi when flight.Phase >= FlightPhase.LandingCleared => Vacated(Released(flight), new TaxiToApron(), keepPhase: false),
            RequestTaxi => Taxi(Released(flight), airport),
            _ => Say(new SayAgainRequest(), flight),
        };
    }

    public AtcDecision Handle(FlightEvent flightEvent, FlightContext flight, AirportContext airport)
    {
        ArgumentNullException.ThrowIfNull(flightEvent);
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(airport);

        switch (flightEvent)
        {
            // The Torre that cleared the takeoff, and is still on the frequency, says the aircraft is off the ground.
            case TookOff tookOff when airport.Unit == ServiceUnit.Tower && flight.Phase == FlightPhase.TakeoffCleared:
                var notice = new AirborneNotice(tookOff.At.UtcDateTime.Minute);

                // The pilot may acknowledge it ("ciente"): kept as pending so that the NLU reads the answer, and taken in silence.
                return new AtcDecision(notice, flight with { Phase = FlightPhase.Airborne, PendingReadback = notice, LastInstruction = notice });

            // The same for the Torre that cleared the landing: the minute, and where to call once the runway is clear.
            case TouchedDown touchedDown when airport.Unit == ServiceUnit.Tower && flight.Phase == FlightPhase.LandingCleared:
                var landed = new LandedNotice(touchedDown.At.UtcDateTime.Minute, airport.Airport.FrequencyOf(AirportFrequencyType.Ground)?.Frequency);

                return new AtcDecision(landed, flight with { Phase = FlightPhase.Landed, PendingReadback = landed, LastInstruction = landed });
            default:
                return new AtcDecision(new NoResponse(), flight);
        }
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

        if (flight.AwaitingCallsign is { } held)
        {
            return Handle(held with { Callsign = flight.Callsign }, flight with { AwaitingCallsign = null }, airport);
        }

        // Only the callsign right after a notice of the controller ("decolado aos ...", "reporte perna base") is the acknowledgement. After
        // "chame Torre ..." it is a call to the unit the pilot is still on, which is invited to go ahead.
        return flight.PendingReadback is AirborneNotice or LandedNotice or ReportBase or ReportOnFinal
            ? new AtcDecision(new NoResponse(), flight with { PendingReadback = null })
            : Say(new GoAhead(), flight);
    }

    // The Torre's part of a departure (MCA 100-16, Art. 126): ready at the holding point, line up and wait; ready again or lined up,
    // cleared for takeoff. The runway is the one the Solo cleared the taxi to.
    private static AtcDecision Departure(Intent intent, FlightContext flight, AirportContext airport)
    {
        if (flight.Phase < FlightPhase.Taxiing || flight.Phase >= FlightPhase.Airborne || intent is ReportTakingOff)
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

    // The Torre's part of an arrival (MCA 100-16, Art. 128 to 132): clearance to the circuit, a report asked at each leg, then the landing.
    // A pilot who reports a leg without the controller having asked gets what the leg calls for, and the phase never goes back.
    private static AtcDecision Arrival(Intent intent, FlightContext flight, AirportContext airport)
    {
        if (intent is ReportGoingAround)
        {
            var noted = new GoAroundNoted();

            return new AtcDecision(noted, flight with { Phase = FlightPhase.Airborne, PendingReadback = null, LastInstruction = noted });
        }

        var runway = intent is not RequestLanding && flight.Phase >= FlightPhase.InCircuit
            ? flight.AssignedRunway ?? airport.RunwayInUse
            : airport.RunwayInUse;

        if (runway is null)
        {
            return Say(new NoRunwayAvailable(), flight);
        }

        return intent switch
        {
            RequestLanding => Clearance(new CircuitClearance(runway, airport.Wind, airport.Qnh), flight, FlightPhase.InCircuit, runway),
            ReportDownwind => Clearance(new ReportBase(), flight, FlightPhase.OnDownwind, runway),
            ReportBaseLeg => Clearance(new ReportOnFinal(), flight, FlightPhase.OnBase, runway),
            _ => Clearance(new LandingClearance(runway, airport.Wind), flight, FlightPhase.LandingCleared, runway),
        };
    }

    private static AtcDecision Clearance(AtcInstruction instruction, FlightContext flight, FlightPhase phase, string runway) =>
        new(instruction, flight with { Phase = phase > flight.Phase || instruction is CircuitClearance ? phase : flight.Phase, PendingReadback = instruction, LastInstruction = instruction, AssignedRunway = runway });

    // After landing and only then: there is no runway to vacate before it. The flight starts over once taken to the apron.
    private static AtcDecision Vacated(FlightContext flight, AtcInstruction instruction, bool keepPhase)
    {
        if (flight.Phase < FlightPhase.Airborne || flight.Phase == FlightPhase.TakeoffCleared)
        {
            return Say(new SayAgainRequest(), flight);
        }

        return new AtcDecision(
            instruction,
            flight with
            {
                Phase = keepPhase ? flight.Phase : FlightPhase.Parked,
                AssignedRunway = keepPhase ? flight.AssignedRunway : null,
                PendingReadback = instruction,
                LastInstruction = instruction,
            });
    }

    private static AtcDecision CallTheTower(FlightContext flight, AirportContext airport) =>
        airport.Airport.FrequencyOf(AirportFrequencyType.Tower) is { } tower
            ? Say(new Atc.CallTheTower(tower.Frequency), flight)
            : Say(new NoTowerAvailable(), flight);

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

        var clearance = new TaxiClearance(airport.RunwayInUse, airport.Qnh);

        // The taxi clearance is what the manual requires to be read back (Art. 12); startup and pushback are read back too, as in practice.
        return new AtcDecision(clearance, flight with { Phase = FlightPhase.Taxiing, PendingReadback = clearance, LastInstruction = clearance, AssignedRunway = airport.RunwayInUse });
    }

    private static AtcDecision Repeat(FlightContext flight)
    {
        return flight.LastInstruction is { } last
            ? new AtcDecision(last, flight)
            : Say(new SayAgainRequest(), flight);
    }

    private static AtcDecision CheckReadback(Readback readback, FlightContext flight, AirportContext airport)
    {
        if (flight.PendingReadback is not { } expected)
        {
            return new AtcDecision(new NoResponse(), flight);
        }

        // The manual does not list the frequency change among what must be read back (Art. 12): pilots do it anyway, and the
        // controller takes it in silence, whatever the words and with or without the callsign, instead of arguing about it.
        if (IsTakenInSilence(expected))
        {
            return new AtcDecision(new NoResponse(), flight with { PendingReadback = null });
        }

        // The aircraft is already doing what was cleared (rolling for the takeoff, taxiing, landed): it was heard, and nagging about the
        // words of the readback would only get in the way.
        if (Situation.AlreadyDoing(expected, flight.Aircraft))
        {
            return new AtcDecision(new NoResponse(), flight with { PendingReadback = null });
        }

        var heard = SpokenTokens.Tokenize(readback.Text);
        var callsignHeard = readback.Callsign is not null || readback.CallsignTail;
        var correct = IsCorrect(expected, heard);

        // What the simulator shows can stand for what the recognizer lost: with the callsign, an aircraft lined up on the runway (or on final)
        // has heard the clearance, whichever words or runway were written; and a readback that is right is accepted without the callsign
        // when the aircraft is where the clearance says.
        var confirmed = Situation.Confirms(expected, flight.Aircraft, airport);

        if ((callsignHeard && (correct || confirmed)) || (correct && confirmed))
        {
            return new AtcDecision(new NoResponse(), flight with { PendingReadback = null });
        }

        // Wrong or incomplete: "negativo" and the right version; the instruction is still waiting for its readback.
        return Say(new RequestReadbackCorrection(expected), flight);
    }

    // What the manual does not list among what must be read back (Art. 12), or says without the pilot having asked: pilots acknowledge
    // it anyway, and the controller takes it in silence, whatever the words and with or without the callsign.
    private static bool IsTakenInSilence(AtcInstruction instruction) => Acknowledgements.NeedsNoReadback(instruction);

    // The taxi readback must have the runway; the startup and pushback ones must say what was approved.
    // A readback is right when it has the action of the instruction and, if it names a runway, the right one. In practice the pilot at the
    // holding point of a runway does not repeat which one ("alinhe e mantenha, PRFAB"): the callsign and the action are what shows that
    // the clearance was heard. A runway that is named and is wrong is still "negativo": that is the mistake that matters.
    private static bool IsCorrect(AtcInstruction expected, IReadOnlyList<string> heard) => expected switch
    {
        TaxiClearance taxi => StatedRunway(heard) is { } runway
            ? RunwayDesignator.AreSame(runway, taxi.Runway)
            : RuleBasedNlu.MentionsTaxi(heard) || RuleBasedNlu.MentionsAuthorization(heard),
        StartupApproved => RuleBasedNlu.MentionsStartup(heard),
        PushbackApproved pushback => RuleBasedNlu.MentionsPushback(heard) && (!pushback.WithStartup || RuleBasedNlu.MentionsStartup(heard)),
        LineUpAndWait lineUp => RuleBasedNlu.MentionsLineUp(heard) && RunwayAgrees(heard, lineUp.Runway),
        TakeoffClearance takeoff => RuleBasedNlu.MentionsTakeoff(heard) && RunwayAgrees(heard, takeoff.Runway),
        TaxiToApron => RuleBasedNlu.MentionsApron(heard),
        CircuitClearance circuit => RuleBasedNlu.MentionsCircuit(heard) && RunwayAgrees(heard, circuit.Runway),
        LandingClearance landing => RuleBasedNlu.MentionsLanding(heard) && RunwayAgrees(heard, landing.Runway),
        _ => true,
    };

    // No runway in the readback is no disagreement; a runway that is there must be the one of the instruction.
    private static bool RunwayAgrees(IReadOnlyList<string> heard, string runway) =>
        StatedRunway(heard) is not { } said || RunwayDesignator.AreSame(said, runway);

    // The runway the pilot named: after the word "pista", or, without it, the first number that is a runway; but not the numbers of the
    // altimeter setting that the clearance may also have ("ajuste de altímetro uno zero uno quatro" is not runway 14).
    private static string? StatedRunway(IReadOnlyList<string> heard)
    {
        var named = RuleBasedNlu.FindRunway(heard);

        return named is not null || heard.Any(t => t is "altimetro" or "qnh") ? named : RuleBasedNlu.FindRunwayInReadback(heard);
    }

    // Answers without moving the flight on. A "REPITA" is not remembered: if the pilot asks to say it again, what is
    // repeated is the last instruction.
    private static AtcDecision Say(AtcInstruction instruction, FlightContext flight) =>
        new(instruction, instruction is SayAgainRequest ? flight : flight with { LastInstruction = instruction });

    private static AtcDecision Issue(AtcInstruction instruction, FlightContext flight, FlightPhase phase) =>
        new(instruction, flight with { Phase = phase > flight.Phase ? phase : flight.Phase, PendingReadback = instruction, LastInstruction = instruction });
}
