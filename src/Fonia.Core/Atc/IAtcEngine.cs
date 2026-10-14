using Fonia.Core.Nlu;

namespace Fonia.Core.Atc;

/// <summary>
/// Decides the controller's answer to what the pilot meant. Deterministic and stateless: the same intent, flight and
/// airport always give the same decision. The speech is made later, from phraseology templates.
/// </summary>
public interface IAtcEngine
{
    AtcDecision Handle(Intent intent, FlightContext flight, AirportContext airport);

    /// <summary>
    /// Decides what the controller says, if anything, about something the telemetry showed. Most events are no business of
    /// the controller that is on the frequency, and then the instruction is <see cref="NoResponse"/> and the flight is unchanged.
    /// </summary>
    AtcDecision Handle(FlightEvent flightEvent, FlightContext flight, AirportContext airport);
}
