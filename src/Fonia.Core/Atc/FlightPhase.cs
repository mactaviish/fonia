namespace Fonia.Core.Atc;

/// <summary>Coarse phase of a flight. Grows with the milestones that need it.</summary>
public enum FlightPhase
{
    Parked = 0,
    PushbackApproved,
    StartupApproved,
    Taxiing,
}
