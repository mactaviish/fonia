using Fonia.Core.Airports;
using Fonia.Core.Nlu;
using Fonia.Core.Phraseology;
using Fonia.Core.Sim;

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
            ReportAtis atis when airport.Unit != ServiceUnit.None
                => Say(new AtisReceived(airport.Report is { } report ? AtisText.Letter(report) : null, atis.Letter, airport.Qnh), Released(flight)),
            _ when airport.Unit is ServiceUnit.None or ServiceUnit.Afis => Say(new NoControlService(airport.Unit), flight with { AwaitingCallsign = null }),

            // The Controle is not played by the app: whatever else the pilot asks of it belongs to the Torre.
            _ when airport.Unit == ServiceUnit.Approach => CallTheTower(Released(flight), airport),

            // On the Torre's frequency the Torre does not do the Solo's job when the airport has a Solo: it says where to call.
            RequestStartup or RequestPushback or RequestTaxi when airport.Unit == ServiceUnit.Tower && airport.Airport.FrequencyOf(AirportFrequencyType.Ground) is { } ground
                => Say(new CallTheSolo(ground.Frequency), Released(flight)),

            // The runway is clear but the way to the apron crosses another one: the Torre clears the crossing, and the Solo comes after it.
            ReportRunwayVacated when airport.Unit == ServiceUnit.Tower && CrossingBeforeTheApron(flight, airport, PositionOf(flight)) is { } crossing
                => Vacated(Released(flight), crossing, keepPhase: true),

            // The runway is clear: the Torre passes the flight to the Solo; the Solo (or a Torre alone) takes it to the apron.
            ReportRunwayVacated when airport.Unit == ServiceUnit.Tower && airport.Airport.FrequencyOf(AirportFrequencyType.Ground) is { } ground
                => Vacated(Released(flight), new HandoffToGround(ground.Frequency), keepPhase: true),
            ReportRunwayVacated => Vacated(Released(flight), new TaxiToApron(), keepPhase: false),

            // The arrival belongs to the Torre; the Solo, asked for it, says where to call.
            RequestLanding or ReportDownwind or ReportBaseLeg or ReportFinal or ReportGoingAround when airport.Unit == ServiceUnit.Tower
                => Arrival(intent, Released(flight), airport),
            RequestLanding or ReportDownwind or ReportBaseLeg or ReportFinal or ReportGoingAround => CallTheTower(Released(flight), airport),

            // The departure belongs to the Torre; the Solo, asked for it, passes the flight on.
            // Held short of a runway the way to the departure runway goes over: the Torre clears the crossing, and the line-up comes after it.
            ReportHoldingShort or RequestDeparture when airport.Unit == ServiceUnit.Tower && CrossingBeforeTheTakeoff(flight, airport) is { } crossing
                => CrossingDecision(crossing, flight, airport),
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
                var notice = new AirborneNotice(tookOff.At.UtcDateTime.Minute, airport.Airport.FrequencyOf(AirportFrequencyType.Approach)?.Frequency);

                // The pilot may acknowledge it ("ciente"): kept as pending so that the NLU reads the answer, and taken in silence.
                return new AtcDecision(notice, flight with { Phase = FlightPhase.Airborne, PendingReadback = notice, LastInstruction = notice });

            // The same for the Torre that cleared the landing: the minute, and where to call once the runway is clear.
            case TouchedDown touchedDown when airport.Unit == ServiceUnit.Tower && flight.Phase == FlightPhase.LandingCleared:
                var landed = new LandedNotice(touchedDown.At.UtcDateTime.Minute, airport.Airport.FrequencyOf(AirportFrequencyType.Ground)?.Frequency);

                return new AtcDecision(landed, flight with { Phase = FlightPhase.Landed, PendingReadback = landed, LastInstruction = landed });

            // The aircraft is clear of the runway it landed on and the way to the apron crosses another: the Torre clears the crossing at once.
            case RunwayVacated vacated when airport.Unit == ServiceUnit.Tower && flight.Phase == FlightPhase.Landed
                && CrossingBeforeTheApron(flight, airport, vacated.Position ?? PositionOf(flight)) is { } crossing:
                return new AtcDecision(crossing, flight with { PendingReadback = crossing, LastInstruction = crossing });

            // The controller sees the aircraft: the next instruction is given as soon as it can be, without the pilot saying "pronto".
            case NearRunway near:
                return Initiative(flight, airport, near.Position ?? PositionOf(flight));
            case TrafficCleared:
                return Initiative(flight, airport, PositionOf(flight));
            default:
                return new AtcDecision(new NoResponse(), flight);
        }
    }

    private static AtcDecision CrossingDecision(CrossRunway crossing, FlightContext flight, AirportContext airport) =>
        new(
            crossing,
            flight with
            {
                Phase = crossing.LineUpRunway is null ? flight.Phase : FlightPhase.LineUpCleared,
                AssignedRunway = flight.AssignedRunway ?? airport.RunwayInUse,
                PendingReadback = crossing,
                LastInstruction = crossing,
            });

    // The aircraft taxiing out is close to a holding point or to the runway. The Solo passes it to the Torre; the Torre clears the crossing,
    // the line-up and then the takeoff, each as soon as it can (nobody ahead). What cannot be given yet is not said: the controller waits, and
    // says "aguarde" only if the pilot calls (see Departure). The same event comes again while the aircraft stays, so nothing is said twice.
    private static AtcDecision Initiative(FlightContext flight, AirportContext airport, GeoPoint? position)
    {
        var silent = new AtcDecision(new NoResponse(), flight);
        var runway = flight.AssignedRunway ?? airport.RunwayInUse;

        if (position is null || airport.Layout is not { } layout || flight.Aircraft is not { OnGround: true } || runway is null
            || flight.Phase < FlightPhase.Taxiing || flight.Phase >= FlightPhase.TakeoffCleared)
        {
            return silent;
        }

        if (!RunwayApproachDetector.IsClose(position, layout, runway))
        {
            return silent;
        }

        if (airport.Unit == ServiceUnit.Ground)
        {
            return flight.Phase == FlightPhase.Taxiing && airport.Airport.FrequencyOf(AirportFrequencyType.Tower) is not null && RunwayApproachDetector.IsNearAHoldingPoint(position, layout)
                ? HoldingShort(flight, airport)
                : silent;
        }

        if (airport.Unit != ServiceUnit.Tower)
        {
            return silent;
        }

        if (flight.Phase < FlightPhase.LineUpCleared)
        {
            if (CrossingBeforeTheTakeoff(flight, airport) is { } crossing)
            {
                return Equals(flight.LastInstruction, crossing) ? silent : CrossingDecision(crossing, flight, airport);
            }

            return !airport.AircraftAhead && NoCrossingLeft(layout, position, runway)
                ? Departure(new ReportHoldingShort(flight.Callsign, null), flight, airport)
                : silent;
        }

        // Told to line up (on its own or with the crossing): the takeoff as soon as the aircraft gets to the runway, lined up or not. The pilot
        // has to have read the line-up back first, unless the aircraft is already on the runway (moving on without answering is not a reason to wait).
        var lineUpSaid = flight.LastInstruction is LineUpAndWait or CrossRunway { LineUpRunway: not null };

        if (!lineUpSaid || airport.AircraftAhead || airport.RunwayBusy || RunwayGeometry.RunwayOf(layout, runway) is not { } found)
        {
            return silent;
        }

        var meters = RunwayGeometry.MetersFromCenterline(found, position.LatitudeDegrees, position.LongitudeDegrees);

        return meters <= RunwayApproachDetector.CloseToRunwayMeters && (flight.PendingReadback is null || meters <= found.WidthMeters / 2 + OnRunwayMarginMeters)
            ? Departure(new ReportLinedUp(flight.Callsign, null), flight, airport)
            : silent;
    }

    // Within this of the edge of the runway the aircraft is on it.
    private const double OnRunwayMarginMeters = 10;

    // The route from here to the departure runway has no runway left to cross.
    private static bool NoCrossingLeft(AirportLayout layout, GeoPoint position, string runway) =>
        TaxiRouter.Find(layout, position, runway) is not TaxiRouteFound { Route: var route } || !route.Crossings.Any(c => !c.Along);

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

        var lined = intent is ReportLinedUp || flight.Phase >= FlightPhase.LineUpCleared;

        // Someone is ahead in the line: told to wait (the controller gives the next instruction by itself once it can).
        if (airport.AircraftAhead)
        {
            return Say(new Standby(), flight);
        }

        // Lined up (said so, or was told to and is ready again), or the runway free for the takeoff already: the takeoff, without the line-up
        // before it. Otherwise (the runway is busy): the line-up.
        if (lined || !airport.RunwayBusy)
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

        // The landings use the arrival runway, which is not the one of the takeoffs at an airport that keeps them apart.
        var runway = intent is not RequestLanding && flight.Phase >= FlightPhase.InCircuit
            ? flight.AssignedRunway ?? airport.RunwayForArrival
            : airport.RunwayForArrival;

        if (runway is null)
        {
            return Say(new NoRunwayAvailable(), flight);
        }

        return intent switch
        {
            RequestLanding { StraightIn: true } => Clearance(new StraightInClearance(runway, airport.Wind, airport.Qnh), flight, FlightPhase.OnBase, runway),
            RequestLanding => Clearance(new CircuitClearance(runway, airport.Wind, airport.Qnh), flight, FlightPhase.InCircuit, runway),
            ReportDownwind => Clearance(new ReportBase(), flight, FlightPhase.OnDownwind, runway),
            ReportBaseLeg => Clearance(new ReportOnFinal(), flight, FlightPhase.OnBase, runway),
            _ => Clearance(new LandingClearance(runway, airport.Wind), flight, FlightPhase.LandingCleared, runway),
        };
    }

    private static AtcDecision Clearance(AtcInstruction instruction, FlightContext flight, FlightPhase phase, string runway) =>
        new(instruction, flight with { Phase = phase > flight.Phase || instruction is CircuitClearance or StraightInClearance ? phase : flight.Phase, PendingReadback = instruction, LastInstruction = instruction, AssignedRunway = runway });

    private static GeoPoint? PositionOf(FlightContext flight) =>
        flight.Aircraft is { OnGround: true } aircraft ? new GeoPoint(aircraft.LatitudeDegrees, aircraft.LongitudeDegrees) : null;

    // The crossing to clear for an aircraft that landed and is clear of the runway: needs the map of the airport and where the aircraft is, and
    // the runway that most of the ways to the apron cross (see TaxiRouter.CrossingToApron). Said once: after it the pilot is on the way to the Solo.
    private static CrossRunway? CrossingBeforeTheApron(FlightContext flight, AirportContext airport, GeoPoint? position)
    {
        if (flight.Phase is not (FlightPhase.LandingCleared or FlightPhase.Landed) || flight.LastInstruction is CrossRunway
            || airport.Layout is not { } layout || position is null)
        {
            return null;
        }

        if (TaxiRouter.CrossingToApron(layout, position) is not { } crossed
            || layout.Runways.FirstOrDefault(r => r.Matches(crossed)) is not { } runway)
        {
            return null;
        }

        // The end said is the one the landings go the same way as: "cruze a pista uno zero esquerda" for an aircraft that landed on the 10R.
        var landing = airport.RunwayForArrival is { } arrival
            ? layout.Runways.SelectMany(r => new[] { r.Low, r.High }).FirstOrDefault(e => RunwayDesignator.AreSame(e.Designator, arrival))
            : null;

        // The aircraft has just left the runway it landed on: a way that goes back over it is the nearest node of the graph being on the far side,
        // not a crossing to clear.
        if (landing is not null
            && (RunwayDesignator.AreSame(runway.Low.Designator, landing.Designator) || RunwayDesignator.AreSame(runway.High.Designator, landing.Designator)))
        {
            return null;
        }

        var end = landing is null
            ? new[] { runway.Low, runway.High }.OrderBy(e => RunwayDesignator.TryParse(e.Designator, out var number, out _) ? number : int.MaxValue).First()
            : new[] { runway.Low, runway.High }.OrderBy(e => HeadingDifference(e.TrueHeadingDegrees, landing.TrueHeadingDegrees)).First();

        return new CrossRunway(RunwayDesignator.Normalize(end.Designator), airport.Airport.FrequencyOf(AirportFrequencyType.Ground)?.Frequency);
    }

    // The aircraft taxiing out is stopped at the holding point of a runway the route to the departure runway goes over (not one it follows):
    // needs the map and the position. The end said is the one that points the way the departure runway does.
    private static CrossRunway? CrossingBeforeTheTakeoff(FlightContext flight, AirportContext airport)
    {
        var position = PositionOf(flight);
        var runway = flight.AssignedRunway ?? airport.RunwayInUse;

        if (flight.Phase < FlightPhase.Taxiing || flight.Phase >= FlightPhase.LineUpCleared
            || airport.Layout is not { } layout || position is null || runway is null)
        {
            return null;
        }

        if (TaxiRouter.Find(layout, position, runway) is not TaxiRouteFound { Route: var route }
            || route.Crossings.FirstOrDefault(c => !c.Along) is not { HoldShortNodeId: { } holdId } first
            || layout.Nodes.FirstOrDefault(n => n.Id == holdId) is not { } hold
            || Geo.DistanceKm(position.LatitudeDegrees, position.LongitudeDegrees, hold.Position.LatitudeDegrees, hold.Position.LongitudeDegrees) * 1000 > RunwayApproachDetector.CloseMeters
            || layout.Runways.FirstOrDefault(r => r.Matches(first.Runway)) is not { } crossed)
        {
            return null;
        }

        var departing = layout.Runways.SelectMany(r => new[] { r.Low, r.High }).FirstOrDefault(e => RunwayDesignator.AreSame(e.Designator, runway));

        var end = departing is null
            ? new[] { crossed.Low, crossed.High }.OrderBy(e => RunwayDesignator.TryParse(e.Designator, out var number, out _) ? number : int.MaxValue).First()
            : new[] { crossed.Low, crossed.High }.OrderBy(e => HeadingDifference(e.TrueHeadingDegrees, departing.TrueHeadingDegrees)).First();

        var crossing = RunwayDesignator.Normalize(end.Designator);

        // Someone ahead in the line: the crossing and then the wait at the holding point; nobody: the crossing and the line-up in one.
        return airport.AircraftAhead ? new CrossRunway(crossing, null, HoldAfter: true) : new CrossRunway(crossing, null, LineUpRunway: runway);
    }

    private static double HeadingDifference(double a, double b) => Math.Abs((a - b + 540) % 360 - 180);

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

        var clearance = new TaxiClearance(airport.RunwayInUse, airport.Qnh, TaxiwaysOfTheRoute(flight, airport, airport.RunwayInUse));

        // The taxi clearance is what the manual requires to be read back (Art. 12); startup and pushback are read back too, as in practice.
        return new AtcDecision(clearance, flight with { Phase = FlightPhase.Taxiing, PendingReadback = clearance, LastInstruction = clearance, AssignedRunway = airport.RunwayInUse });
    }

    // The taxiways to say when the airport has a map, the aircraft is on the ground at a place it shows, and the route to the holding point
    // is safe to say: found, with names, and over no runway (the crossing is not said yet). Anything else is the plain clearance.
    private static IReadOnlyList<string>? TaxiwaysOfTheRoute(FlightContext flight, AirportContext airport, string runway)
    {
        if (airport.Layout is not { } layout || flight.Aircraft is not { OnGround: true } aircraft)
        {
            return null;
        }

        var found = TaxiRouter.Find(layout, new GeoPoint(aircraft.LatitudeDegrees, aircraft.LongitudeDegrees), runway);

        return found is TaxiRouteFound { Route: { Crossings.Count: 0, Taxiways.Count: > 0 } route } ? route.Taxiways : null;
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
        StraightInClearance straightIn => RuleBasedNlu.MentionsStraightIn(heard) && RunwayAgrees(heard, straightIn.Runway),
        LandingClearance landing => RuleBasedNlu.MentionsLanding(heard) && RunwayAgrees(heard, landing.Runway),
        // Two runways are named in "cruzando pista X, alinhando pista Y": only that both things were said is judged.
        CrossRunway { LineUpRunway: not null } => RuleBasedNlu.MentionsCrossing(heard) && RuleBasedNlu.MentionsLineUp(heard),
        CrossRunway crossing => RuleBasedNlu.MentionsCrossing(heard) && RunwayAgrees(heard, crossing.Runway),
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

        return named is not null || heard.Any(t => t is "altimetro" or "qnh") ? named : RuleBasedNlu.FindRunwayInReadback(BeforeTheTaxiways(heard));
    }

    // The digits of a taxiway name ("via taxiway bravo dois") are not a runway: what the pilot says from "via" or "taxiway" on is left out.
    private static IReadOnlyList<string> BeforeTheTaxiways(IReadOnlyList<string> heard)
    {
        var at = heard.ToList().FindIndex(t => t is "via" or "taxiway");

        return at < 0 ? heard : [.. heard.Take(at)];
    }

    // Answers without moving the flight on. A "REPITA" is not remembered: if the pilot asks to say it again, what is
    // repeated is the last instruction.
    private static AtcDecision Say(AtcInstruction instruction, FlightContext flight) =>
        new(instruction, instruction is SayAgainRequest ? flight : flight with { LastInstruction = instruction });

    private static AtcDecision Issue(AtcInstruction instruction, FlightContext flight, FlightPhase phase) =>
        new(instruction, flight with { Phase = phase > flight.Phase ? phase : flight.Phase, PendingReadback = instruction, LastInstruction = instruction });
}
