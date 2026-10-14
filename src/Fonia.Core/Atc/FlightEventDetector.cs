using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// Turns the stream of snapshots into the events the controller speaks about. A takeoff is the aircraft leaving the ground and
/// staying off it for a moment: no speed is asked, since a helicopter lifts off at zero knots, and the wait is what tells a takeoff
/// from a bounce or a glitch of the simulator. An aircraft that is already flying when the app starts has no takeoff to announce.
/// The engine decides whether the event matters to the flight.
/// </summary>
public sealed class FlightEventDetector
{
    private static readonly TimeSpan StayOffTheGround = TimeSpan.FromSeconds(5);

    private bool _seenOnTheGround;
    private DateTimeOffset? _leftTheGroundAt;

    /// <returns>The event this snapshot shows, or null. A takeoff is reported with the time the aircraft left the ground.</returns>
    public FlightEvent? Observe(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.OnGround)
        {
            _seenOnTheGround = true;
            _leftTheGroundAt = null;
            return null;
        }

        if (!_seenOnTheGround)
        {
            return null;
        }

        _leftTheGroundAt ??= state.Timestamp;

        if (state.Timestamp - _leftTheGroundAt < StayOffTheGround)
        {
            return null;
        }

        var tookOff = new TookOff(_leftTheGroundAt.Value);
        _seenOnTheGround = false;
        _leftTheGroundAt = null;

        return tookOff;
    }
}
