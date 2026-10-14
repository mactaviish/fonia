using System.Globalization;

namespace Fonia.Audio;

/// <summary>Names of the keys (and mouse side buttons) usable as the push-to-talk, and their Windows virtual-key codes.</summary>
public static class PushToTalkKey
{
    public const string DefaultName = "RControl";

    /// <summary>A few names for the help text; many more parse (letters, digits, F1 to F24).</summary>
    public static IReadOnlyList<string> ExampleNames { get; } =
        ["RControl", "LControl", "RShift", "RAlt", "CapsLock", "Space", "ScrollLock", "Insert", "F8", "Mouse4", "Mouse5"];

    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["control"] = 0x11,
        ["ctrl"] = 0x11,
        ["lcontrol"] = 0xA2,
        ["lctrl"] = 0xA2,
        ["leftcontrol"] = 0xA2,
        ["leftctrl"] = 0xA2,
        ["rcontrol"] = 0xA3,
        ["rctrl"] = 0xA3,
        ["rightcontrol"] = 0xA3,
        ["rightctrl"] = 0xA3,
        ["shift"] = 0x10,
        ["lshift"] = 0xA0,
        ["leftshift"] = 0xA0,
        ["rshift"] = 0xA1,
        ["rightshift"] = 0xA1,
        ["alt"] = 0x12,
        ["lalt"] = 0xA4,
        ["leftalt"] = 0xA4,
        ["ralt"] = 0xA5,
        ["rightalt"] = 0xA5,
        ["capslock"] = 0x14,
        ["space"] = 0x20,
        ["tab"] = 0x09,
        ["insert"] = 0x2D,
        ["home"] = 0x24,
        ["end"] = 0x23,
        ["pageup"] = 0x21,
        ["pagedown"] = 0x22,
        ["scrolllock"] = 0x91,
        ["pause"] = 0x13,
        ["numlock"] = 0x90,
        ["mouse4"] = 0x05,
        ["mouse5"] = 0x06,
    };

    /// <summary>
    /// Reads a key name, ignoring case, spaces, hyphens and underscores ("R-Control" and "right_ctrl" are fine).
    /// </summary>
    public static bool TryParse(string? name, out int virtualKey)
    {
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = new string(name.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '_')).ToArray());

        if (Named.TryGetValue(normalized, out virtualKey))
        {
            return true;
        }

        if (normalized.Length == 1 && char.IsAsciiLetterOrDigit(normalized[0]))
        {
            virtualKey = char.ToUpperInvariant(normalized[0]); // VK_A..VK_Z and VK_0..VK_9 are the ASCII codes
            return true;
        }

        if (normalized.Length is >= 2 and <= 3
            && (normalized[0] is 'F' or 'f')
            && int.TryParse(normalized.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= 24)
        {
            virtualKey = 0x70 + number - 1; // VK_F1
            return true;
        }

        virtualKey = 0;
        return false;
    }
}
