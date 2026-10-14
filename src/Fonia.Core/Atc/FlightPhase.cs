namespace Fonia.Core.Atc;

/// <summary>Coarse phase of a flight. Grows with the milestones that need it.</summary>
public enum FlightPhase
{
    Parked = 0,
    PushbackApproved,
    StartupApproved,
    Taxiing,

    /// <summary>The Solo reported the holding point and passed the flight to the Torre: from here the Torre talks.</summary>
    HandedToTower,

    /// <summary>The Torre told the flight to line up and wait on the runway.</summary>
    LineUpCleared,

    /// <summary>The Torre cleared the takeoff.</summary>
    TakeoffCleared,
}
