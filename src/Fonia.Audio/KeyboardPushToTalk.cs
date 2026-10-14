using System.Runtime.InteropServices;
using Fonia.Core.Audio;

namespace Fonia.Audio;

/// <summary>
/// Push-to-talk on a keyboard key or a mouse side button, seen system-wide (the simulator keeps the focus).
/// It asks Windows whether the key is down about every 15 ms; nothing is hooked and no admin rights are needed.
/// Windows only.
/// </summary>
public static partial class KeyboardPushToTalk
{
    private const int KeyDownMask = 0x8000;

    /// <summary>Creates the push-to-talk for a virtual-key code (see <see cref="PushToTalkKey.TryParse"/>).</summary>
    public static PolledPushToTalk Create(int virtualKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The keyboard push-to-talk works only on Windows.");
        }

        return new PolledPushToTalk(() => (GetAsyncKeyState(virtualKey) & KeyDownMask) != 0);
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);
}
