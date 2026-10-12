namespace Fonia.Core.Atc;

/// <summary>Per-flight state used by the NLU and the ATC engine.</summary>
/// <param name="Callsign">Normalized callsign, e.g. "PRABC".</param>
/// <param name="AirportIcao">ICAO code of the airport the flight is currently handled at, if any.</param>
/// <param name="PendingReadback">Last instruction issued that still awaits a pilot readback.</param>
public sealed record FlightContext(
    string Callsign,
    FlightPhase Phase,
    string? AirportIcao,
    AtcInstruction? PendingReadback);
