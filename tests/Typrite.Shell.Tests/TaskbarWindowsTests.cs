using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;
using Typrite.Shell.Taskbar;

namespace Typrite.Shell.Tests;

public class TaskbarWindowsTests
{
    private static readonly WindowObservation AppWindow = new(
        1, new PhysicalRect(0, 0, 800, 600), IsVisible: true, IsCloaked: false,
        IsMinimized: false, IsToolWindow: false, IsOwnWindow: false, IsShellWindow: false,
        App: null, IsBorderless: false);

    [Fact]
    public void Switcher_includes_normal_and_minimized_apps_but_excludes_shell_and_helper_windows()
    {
        var minimized = AppWindow with { Handle = 2, IsMinimized = true };
        WindowObservation[] windows =
        [
            AppWindow, minimized,
            AppWindow with { IsVisible = false },
            AppWindow with { IsCloaked = true },
            AppWindow with { IsToolWindow = true },
            AppWindow with { IsOwnWindow = true },
            AppWindow with { IsShellWindow = true },
        ];

        Assert.Equal([AppWindow, minimized], TaskbarWindows.Select(windows));
    }

    [Theory]
    [InlineData(96, 258)]
    [InlineData(120, 323)]
    [InlineData(144, 387)]
    public void Combined_demo_reservation_preserves_ad_height_and_scales_taskbar(uint dpi, int heightPx)
    {
        var rect = AppBarGeometry.RequestedBottomRect(new PhysicalRect(0, 0, 1920, 1080), 210 + 48, dpi);

        Assert.Equal(heightPx, rect.Height);
        Assert.Equal(1080, rect.Bottom);
    }
}
