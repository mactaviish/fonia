namespace Fonia.Core.Audio;

/// <summary>The push-to-talk control: the pilot holds it while speaking on the radio.</summary>
public interface IPushToTalk : IDisposable
{
    bool IsPressed { get; }

    /// <summary>Raised once when the control goes down, on an arbitrary thread.</summary>
    event EventHandler? Pressed;

    /// <summary>
    /// Raised once when the control comes up, on an arbitrary thread. Stopping while pressed also raises it,
    /// so a recording never waits for a release that cannot come.
    /// </summary>
    event EventHandler? Released;

    /// <summary>Starts watching the control. Does nothing if it is already running.</summary>
    void StartWatching();

    /// <summary>Stops watching the control, releasing it first if it is pressed.</summary>
    void StopWatching();
}
