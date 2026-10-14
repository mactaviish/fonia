namespace Fonia.Core.Audio;

/// <summary>
/// A push-to-talk that asks <c>isDown</c> at a fixed interval (a few milliseconds is imperceptible for a radio
/// key). Polling needs no hook, no message loop and no privileges, and it sees keys while the simulator has focus.
/// The question itself (a keyboard key, a joystick button) is the caller's, which keeps this class free of the OS.
/// </summary>
public sealed class PolledPushToTalk(Func<bool> isDown, TimeSpan? interval = null, TimeProvider? timeProvider = null) : IPushToTalk
{
    private readonly Func<bool> _isDown = isDown ?? throw new ArgumentNullException(nameof(isDown));
    private readonly TimeSpan _interval = interval is { } value && value > TimeSpan.Zero ? value : TimeSpan.FromMilliseconds(15);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private bool _disposed;

    public bool IsPressed { get; private set; }

    public event EventHandler? Pressed;

    public event EventHandler? Released;

    public void StartWatching()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_loop is not null)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _loop = Task.Run(() => LoopAsync(cancellation.Token));
        }
    }

    public void StopWatching()
    {
        CancellationTokenSource? cancellation;

        lock (_gate)
        {
            cancellation = _cancellation;
            _loop = null;
            _cancellation = null;
        }

        // Not waiting for the loop: a handler of Pressed or Released may stop (or dispose) from the loop's own
        // thread, and waiting there would deadlock. A poll already in flight can raise one last event.
        cancellation?.Cancel();
        ReleaseIfPressed();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopWatching();
        _disposed = true;
    }

    /// <summary>Samples the control once and raises <see cref="Pressed"/> or <see cref="Released"/> on a change.</summary>
    public void Poll()
    {
        if (_disposed)
        {
            return;
        }

        var down = _isDown();
        bool raisePressed;
        bool raiseReleased;

        lock (_gate)
        {
            raisePressed = down && !IsPressed;
            raiseReleased = !down && IsPressed;
            IsPressed = down;
        }

        if (raisePressed)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (raiseReleased)
        {
            Released?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                Poll();
            }
        }
        catch (OperationCanceledException)
        {
            // StopWatching asked the loop to end.
        }
    }

    private void ReleaseIfPressed()
    {
        bool wasPressed;

        lock (_gate)
        {
            wasPressed = IsPressed;
            IsPressed = false;
        }

        if (wasPressed)
        {
            Released?.Invoke(this, EventArgs.Empty);
        }
    }
}
