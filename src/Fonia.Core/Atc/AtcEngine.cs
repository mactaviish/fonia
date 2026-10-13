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
            Unknown => Say(new SayAgainRequest(), flight),
            CallsignOnly called => Called(called, flight, airport),
            SayAgain => Repeat(flight),
            Readback readback => CheckReadback(readback, flight),
            _ when intent.Callsign is null => new AtcDecision(new RequestCallsign(), flight with { AwaitingCallsign = intent }),
            _ when airport.Unit is ServiceUnit.None or ServiceUnit.Afis => Say(new NoControlService(airport.Unit), flight with { AwaitingCallsign = null }),
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

    private static AtcDecision Taxi(FlightContext flight, AirportContext airport)
    {
        if (airport.RunwayInUse is null)
        {
            return Say(new NoRunwayAvailable(), flight);
        }

        var clearance = new TaxiClearance(airport.RunwayInUse);

        // The taxi clearance is what the manual requires to be read back (Art. 12); startup and pushback are read back too, as in practice.
        return new AtcDecision(clearance, flight with { Phase = FlightPhase.Taxiing, PendingReadback = clearance, LastInstruction = clearance });
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

        var heard = SpokenTokens.Tokenize(readback.Text);

        if (IsCorrect(expected, heard))
        {
            return new AtcDecision(new NoResponse(), flight with { PendingReadback = null });
        }

        // Wrong or incomplete: "negativo" and the right version; the instruction is still waiting for its readback.
        return Say(new RequestReadbackCorrection(expected), flight);
    }

    // The taxi readback must have the runway; the startup and pushback ones must say what was approved.
    private static bool IsCorrect(AtcInstruction expected, IReadOnlyList<string> heard) => expected switch
    {
        TaxiClearance taxi => RuleBasedNlu.FindRunway(heard) is { } runway && RunwayDesignator.AreSame(runway, taxi.Runway),
        StartupApproved => RuleBasedNlu.MentionsStartup(heard),
        PushbackApproved pushback => RuleBasedNlu.MentionsPushback(heard) && (!pushback.WithStartup || RuleBasedNlu.MentionsStartup(heard)),
        _ => true,
    };

    // Answers without moving the flight on. A "REPITA" is not remembered: if the pilot asks to say it again, what is
    // repeated is the last instruction.
    private static AtcDecision Say(AtcInstruction instruction, FlightContext flight) =>
        new(instruction, instruction is SayAgainRequest ? flight : flight with { LastInstruction = instruction });

    private static AtcDecision Issue(AtcInstruction instruction, FlightContext flight, FlightPhase phase) =>
        new(instruction, flight with { Phase = phase > flight.Phase ? phase : flight.Phase, PendingReadback = instruction, LastInstruction = instruction });
}
