using Typrite.Shell.Geometry;

namespace Typrite.Shell.Tests;

public class AppBarGeometryTests
{
    private static readonly PhysicalRect Monitor1080p = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(210, 96, 210)]
    [InlineData(210, 120, 263)]
    [InlineData(210, 144, 315)]
    [InlineData(210, 192, 420)]
    public void DipToPhysical_scales_by_dpi_and_rounds(double dip, uint dpi, int expected)
    {
        Assert.Equal(expected, DpiScale.DipToPhysical(dip, dpi));
    }

    [Fact]
    public void RequestedBottomRect_spans_monitor_width_at_bottom_edge()
    {
        var rect = AppBarGeometry.RequestedBottomRect(Monitor1080p, reservationDip: 210, dpi: 96);

        Assert.Equal(new PhysicalRect(0, 870, 1920, 1080), rect);
    }

    [Fact]
    public void RequestedBottomRect_uses_monitor_origin_for_secondary_display()
    {
        var secondary = new PhysicalRect(1920, -200, 4480, 1240);

        var rect = AppBarGeometry.RequestedBottomRect(secondary, reservationDip: 100, dpi: 96);

        Assert.Equal(new PhysicalRect(1920, 1140, 4480, 1240), rect);
    }

    [Fact]
    public void RequestedBottomRect_accepts_physical_height_directly()
    {
        var rect = AppBarGeometry.RequestedBottomRect(Monitor1080p, heightPx: 263);

        Assert.Equal(new PhysicalRect(0, 817, 1920, 1080), rect);
    }

    [Fact]
    public void FitHeightToAdjusted_keeps_height_when_windows_moves_rect_above_taskbar()
    {
        var adjustedByShell = new PhysicalRect(0, 0, 1920, 1032); // taskbar already took 48px

        var rect = AppBarGeometry.FitHeightToAdjusted(adjustedByShell, heightPx: 210);

        Assert.Equal(new PhysicalRect(0, 822, 1920, 1032), rect);
    }

    [Fact]
    public void RequestedBottomRect_rejects_non_positive_reservation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AppBarGeometry.RequestedBottomRect(Monitor1080p, reservationDip: 0, dpi: 96));
    }

    [Fact]
    public void PhysicalRect_exposes_width_and_height()
    {
        var rect = new PhysicalRect(10, 20, 110, 70);

        Assert.Equal(100, rect.Width);
        Assert.Equal(50, rect.Height);
    }
}
