using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// Turns the stream of snapshots into the events the controller speaks about. It only compares each snapshot with the one before,
/// so an aircraft that is already flying when the app starts, or that bounces on landing, does not announce a takeoff by itself:
/// the engine decides whether the event matters to the flight.
/// </summary>
public sealed class FlightEventDetector
{
    // Below this the "in the air" of the simulator is a bounce or a glitch on the apron, not a takeoff.
    private const double MinimumTakeoffKnots = 40;

    private bool? _wasOnGround;

    /// <returns>The event this snapshot shows, or null.</returns>
    public FlightEvent? Observe(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var tookOff = _wasOnGround == true && !state.OnGround && state.GroundSpeedKnots >= MinimumTakeoffKnots;
        _wasOnGround = state.OnGround;

        return tookOff ? new TookOff(state.Timestamp) : null;
    }
}
