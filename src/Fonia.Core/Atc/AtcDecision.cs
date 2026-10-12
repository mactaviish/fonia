namespace Fonia.Core.Atc;

/// <summary>Result of the ATC engine: what to instruct and the flight context afterwards.</summary>
public sealed record AtcDecision(AtcInstruction Instruction, FlightContext NextContext);
