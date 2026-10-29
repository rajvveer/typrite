using Typrite.Shell.Displays;

namespace Typrite.Shell.Tests;

public class DisplayKeyTests
{
    private const string Path1 = @"\\?\DISPLAY#DELA1C3#5&2b1f0a2&0&UID4357#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    [Fact]
    public void Same_edid_yields_same_key_regardless_of_device_path()
    {
        var a = DisplayKey.From(Path1, new EdidIdentity("DEL", 0xA1C3, "ABC123"));
        var b = DisplayKey.From(@"\\?\DISPLAY#DELA1C3#9&1111111&0&UID9999#{...}", new EdidIdentity("DEL", 0xA1C3, "ABC123"));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_serial_yields_different_key()
    {
        var a = DisplayKey.From(Path1, new EdidIdentity("DEL", 0xA1C3, "ABC123"));
        var b = DisplayKey.From(Path1, new EdidIdentity("DEL", 0xA1C3, "XYZ789"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Missing_edid_falls_back_to_device_path_without_instance_noise()
    {
        var a = DisplayKey.From(Path1, edid: null);
        var b = DisplayKey.From(@"\\?\DISPLAY#DELA1C3#7&deadbeef&0&UID1#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", edid: null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Key_is_short_lowercase_hex()
    {
        var key = DisplayKey.From(Path1, new EdidIdentity("DEL", 0xA1C3, "ABC123"));

        Assert.Matches("^[0-9a-f]{32}$", key.Value);
    }

    [Fact]
    public void Blank_device_path_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DisplayKey.From("  ", edid: null));
    }
}
