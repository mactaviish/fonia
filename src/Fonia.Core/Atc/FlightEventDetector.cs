using Fonia.Core.Sim;

namespace Fonia.Core.Atc;

/// <summary>
/// Turns the stream of snapshots into the events the controller speaks about. A takeoff is the aircraft leaving the ground and
/// staying off it for a moment, a landing is it touching down and staying on it: no speed is asked, since a helicopter lifts off at
/// zero knots, and the wait is what tells the real thing from a bounce or a glitch of the simulator. An aircraft that taxis never
/// lands, and one that is already flying when the app starts has no takeoff to announce but lands like any other. The engine decides
/// whether the event matters to the flight.
/// </summary>
public sealed class FlightEventDetector
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private bool _seenOnTheGround;
    private bool _flying;
    private DateTimeOffset? _leftTheGroundAt;
    private DateTimeOffset? _flyingSince;
    private DateTimeOffset? _touchedDownAt;

    /// <returns>The event this snapshot shows, or null. Each is reported with the time the aircraft left or touched the ground.</returns>
    public FlightEvent? Observe(AircraftState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.OnGround ? OnTheGround(state) : InTheAir(state);
    }

    private TouchedDown? OnTheGround(AircraftState state)
    {
        _seenOnTheGround = true;
        _leftTheGroundAt = null;
        _flyingSince = null;

        if (!_flying)
        {
            return null;
        }

        _touchedDownAt ??= state.Timestamp;

        if (state.Timestamp - _touchedDownAt < Settle)
        {
            return null;
        }

        var landed = new TouchedDown(_touchedDownAt.Value);
        _flying = false;
        _touchedDownAt = null;

        return landed;
    }

    private TookOff? InTheAir(AircraftState state)
    {
        _touchedDownAt = null;

        if (!_seenOnTheGround)
        {
            // Already flying when the app started: no takeoff, but after a while it counts as flying, so that it can land.
            _flyingSince ??= state.Timestamp;
            _flying |= state.Timestamp - _flyingSince >= Settle;
            return null;
        }

        _leftTheGroundAt ??= state.Timestamp;

        if (state.Timestamp - _leftTheGroundAt < Settle)
        {
            return null;
        }

        var tookOff = new TookOff(_leftTheGroundAt.Value);
        _seenOnTheGround = false;
        _flying = true;
        _leftTheGroundAt = null;

        return tookOff;
    }
}
