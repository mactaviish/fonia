using Fonia.Core.Nlu;
using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>Per-flight state used by the NLU and the ATC engine.</summary>
/// <param name="Callsign">Normalized callsign, e.g. "PRABC".</param>
/// <param name="AirportIcao">ICAO code of the airport the flight is currently handled at, if any.</param>
/// <param name="PendingReadback">Last instruction issued that still awaits a pilot readback.</param>
/// <param name="LastInstruction">The last instruction the controller gave, for when the pilot asks to say it again.</param>
/// <param name="AwaitingCallsign">A request the pilot made without the right callsign, held until they say it.</param>
/// <param name="Aircraft">What the simulator shows of the aircraft now (where it is, which way it points, how fast), when there is one: it lets the engine judge a readback by the situation. Null without a simulator.</param>
/// <param name="AssignedRunway">The runway the Solo cleared the taxi to, which the Torre then lines the flight up on.</param>
public sealed record FlightContext(
    string Callsign,
    FlightPhase Phase,
    string? AirportIcao,
    AtcInstruction? PendingReadback,
    AtcInstruction? LastInstruction = null,
    Intent? AwaitingCallsign = null,
    string? AssignedRunway = null,
    AircraftState? Aircraft = null);
