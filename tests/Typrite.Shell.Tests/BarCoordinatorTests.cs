using Typrite.Shell.AppBar;
using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;

namespace Typrite.Shell.Tests;

public class BarCoordinatorTests
{
    private static readonly PhysicalRect Monitor = new(0, 0, 1920, 1080);

    private sealed class FakeHost : IAppBarHost
    {
        public List<string> Calls { get; } = [];
        public PhysicalRect Granted { get; set; } = new(0, 822, 1920, 1032);

        public event EventHandler<bool>? FullscreenAppChanged;
        public event EventHandler? PositionChanged;

        public PhysicalRect Register(nint hwnd, PhysicalRect monitor, int heightPx)
        {
            Calls.Add($"register:{heightPx}");
            return Granted;
        }

        public PhysicalRect Reposition(PhysicalRect monitor, int heightPx)
        {
            Calls.Add($"reposition:{heightPx}");
            return Granted;
        }

        public void Unregister() => Calls.Add("unregister");

        public void Dispose() => Calls.Add("dispose");

        public void RaiseFullscreen(bool started) => FullscreenAppChanged?.Invoke(this, started);

        public void RaisePositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeSurface : IBarSurface
    {
        public List<string> Calls { get; } = [];

        public void SendToBottom() => Calls.Add("bottom");

        public void BringToTop() => Calls.Add("top");
    }

    private static (BarCoordinator Bar, FakeHost Host, FakeSurface Surface) Attached(uint dpi = 96)
    {
        var host = new FakeHost();
        var surface = new FakeSurface();
        var bar = new BarCoordinator(host, surface, reservationDip: 210);
        bar.Attach(hwnd: 0x42, Monitor, dpi);
        return (bar, host, surface);
    }

    [Fact]
    public void Attach_registers_with_dpi_scaled_height_and_reports_normal_mode()
    {
        var (bar, host, _) = Attached(dpi: 120);

        Assert.Equal(["register:263"], host.Calls);
        Assert.Equal(DisplayMode.NormalReserved, bar.Mode);
        Assert.Equal(host.Granted, bar.Bounds);
    }

    [Fact]
    public void Fullscreen_app_yields_and_drops_bar_to_bottom()
    {
        var (bar, host, surface) = Attached();

        host.RaiseFullscreen(started: true);

        Assert.Equal(DisplayMode.YieldedFullscreen, bar.Mode);
        Assert.Equal(["bottom"], surface.Calls);
    }

    [Fact]
    public void Fullscreen_ending_restores_normal_and_brings_bar_back()
    {
        var (bar, host, surface) = Attached();
        host.RaiseFullscreen(started: true);

        host.RaiseFullscreen(started: false);

        Assert.Equal(DisplayMode.NormalReserved, bar.Mode);
        Assert.Equal(["bottom", "top"], surface.Calls);
    }

    [Fact]
    public void Fullscreen_end_without_start_is_ignored()
    {
        var (bar, host, surface) = Attached();

        host.RaiseFullscreen(started: false);

        Assert.Equal(DisplayMode.NormalReserved, bar.Mode);
        Assert.Empty(surface.Calls);
    }

    [Fact]
    public void Position_change_repositions_with_current_height()
    {
        var (bar, host, _) = Attached();
        host.Granted = new PhysicalRect(0, 870, 1920, 1080);

        host.RaisePositionChanged();

        Assert.Equal(["register:210", "reposition:210"], host.Calls);
        Assert.Equal(host.Granted, bar.Bounds);
    }

    [Fact]
    public void Dpi_change_repositions_with_new_height()
    {
        var (bar, host, _) = Attached();

        bar.DpiChanged(144);

        Assert.Equal(["register:210", "reposition:315"], host.Calls);
    }

    [Fact]
    public void Shell_restart_re_registers_from_scratch()
    {
        var (bar, host, _) = Attached();
        host.Calls.Clear();

        bar.ShellRestarted();

        Assert.Equal(["unregister", "register:210"], host.Calls);
        Assert.Equal(DisplayMode.NormalReserved, bar.Mode);
    }

    [Fact]
    public void Mode_changes_are_observable()
    {
        var host = new FakeHost();
        var bar = new BarCoordinator(host, new FakeSurface(), reservationDip: 210);
        var modes = new List<DisplayMode>();
        bar.ModeChanged += (_, m) => modes.Add(m);

        bar.Attach(0x42, Monitor, 96);
        host.RaiseFullscreen(true);

        Assert.Equal([DisplayMode.NormalReserved, DisplayMode.FullscreenDetected, DisplayMode.YieldedFullscreen], modes);
    }

    [Fact]
    public void Detach_unregisters_once()
    {
        var (bar, host, _) = Attached();

        bar.Detach();
        bar.Detach();

        Assert.Equal(["register:210", "unregister"], host.Calls);
    }
}
