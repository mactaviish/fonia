namespace Fonia.Audio.Tests;

public class PushToTalkKeyTests
{
    [Theory]
    [InlineData("RControl", 0xA3)]
    [InlineData("rcontrol", 0xA3)]
    [InlineData("R-Control", 0xA3)]
    [InlineData("right_ctrl", 0xA3)]
    [InlineData("LControl", 0xA2)]
    [InlineData("Control", 0x11)]
    [InlineData("RShift", 0xA1)]
    [InlineData("LAlt", 0xA4)]
    [InlineData("RAlt", 0xA5)]
    [InlineData("CapsLock", 0x14)]
    [InlineData("Space", 0x20)]
    [InlineData("ScrollLock", 0x91)]
    [InlineData("Insert", 0x2D)]
    [InlineData("F1", 0x70)]
    [InlineData("f12", 0x7B)]
    [InlineData("F24", 0x87)]
    [InlineData("A", 0x41)]
    [InlineData("z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    [InlineData("Mouse4", 0x05)]
    [InlineData("Mouse5", 0x06)]
    [InlineData("  Space  ", 0x20)]
    public void It_maps_key_names_to_windows_virtual_keys(string name, int expected)
    {
        PushToTalkKey.TryParse(name, out var key).ShouldBeTrue();
        key.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("F25")]
    [InlineData("F0")]
    [InlineData("Banana")]
    [InlineData("AB")]
    [InlineData("Mouse1")]
    public void It_rejects_unknown_names(string name)
    {
        PushToTalkKey.TryParse(name, out _).ShouldBeFalse();
    }

    [Fact]
    public void It_rejects_null()
    {
        PushToTalkKey.TryParse(null, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_default_is_the_right_control_key()
    {
        PushToTalkKey.TryParse(PushToTalkKey.DefaultName, out var key).ShouldBeTrue();
        key.ShouldBe(0xA3);
    }

    [Fact]
    public void The_help_text_lists_names_that_all_parse()
    {
        foreach (var name in PushToTalkKey.ExampleNames)
        {
            PushToTalkKey.TryParse(name, out _).ShouldBeTrue(name);
        }
    }
}
