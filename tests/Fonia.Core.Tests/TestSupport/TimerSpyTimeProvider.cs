using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Fonia.Core.Tests.TestSupport;

/// <summary>
/// A fake clock that also tells the test every wait the code under test asks for, so a test can
/// assert the exact delay and advance time only once the timer exists (no sleeping, no races).
/// </summary>
internal sealed class TimerSpyTimeProvider(FakeTimeProvider inner) : TimeProvider
{
    private readonly Channel<TimeSpan> _requestedDelays = Channel.CreateUnbounded<TimeSpan>();

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    /// <summary>True if the code created a timer that the test has not consumed yet.</summary>
    public bool HasPendingRequest => _requestedDelays.Reader.TryPeek(out _);

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _requestedDelays.Writer.TryWrite(dueTime);
        return inner.CreateTimer(callback, state, dueTime, period);
    }

    /// <summary>Waits for the next timer the code creates and returns how long it asked to wait.</summary>
    public async Task<TimeSpan> NextRequestedDelayAsync(CancellationToken ct)
    {
        return await _requestedDelays.Reader.ReadAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct);
    }

    public void Advance(TimeSpan by) => inner.Advance(by);
}
