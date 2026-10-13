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
            SayAgain => Repeat(flight),
            Readback readback => CheckReadback(readback, flight),
            _ when airport.Unit is ServiceUnit.None or ServiceUnit.Afis => Say(new NoControlService(airport.Unit), flight),
            RequestStartup => Issue(new StartupApproved(), flight, FlightPhase.StartupApproved),
            RequestPushback pushback => Issue(new PushbackApproved(pushback.WithStartup), flight, pushback.WithStartup ? FlightPhase.StartupApproved : FlightPhase.PushbackApproved),
            RequestTaxi => Taxi(flight, airport),
            _ => Say(new SayAgainRequest(), flight),
        };
    }

    private static AtcDecision Taxi(FlightContext flight, AirportContext airport)
    {
        if (airport.RunwayInUse is null)
        {
            return Say(new NoRunwayAvailable(), flight);
        }

        var clearance = new TaxiClearance(airport.RunwayInUse);

        // The taxi clearance is what the manual requires to be read back (Art. 12).
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
        if (flight.PendingReadback is not TaxiClearance expected)
        {
            return new AtcDecision(new NoResponse(), flight);
        }

        var heard = RuleBasedNlu.FindRunway(SpokenTokens.Tokenize(readback.Text));

        if (heard is not null && RunwayDesignator.AreSame(heard, expected.Runway))
        {
            return new AtcDecision(new NoResponse(), flight with { PendingReadback = null });
        }

        // Wrong or incomplete: "negativo" and the right version; the clearance is still waiting for its readback.
        return Say(new RequestReadbackCorrection(expected), flight);
    }

    // Answers without moving the flight on. A "REPITA" is not remembered: if the pilot asks to say it again, what is
    // repeated is the last instruction.
    private static AtcDecision Say(AtcInstruction instruction, FlightContext flight) =>
        new(instruction, instruction is SayAgainRequest ? flight : flight with { LastInstruction = instruction });

    private static AtcDecision Issue(AtcInstruction instruction, FlightContext flight, FlightPhase phase) =>
        new(instruction, flight with { Phase = phase > flight.Phase ? phase : flight.Phase, LastInstruction = instruction });
}
