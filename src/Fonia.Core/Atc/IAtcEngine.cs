using Fonia.Core.Nlu;

namespace Fonia.Core.Atc;

/// <summary>
/// Decides the controller's answer to what the pilot meant. Deterministic and stateless: the same intent, flight and
/// airport always give the same decision. The speech is made later, from phraseology templates.
/// </summary>
public interface IAtcEngine
{
    AtcDecision Handle(Intent intent, FlightContext flight, AirportContext airport);
}
