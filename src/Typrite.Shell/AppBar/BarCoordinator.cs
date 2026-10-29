using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;

namespace Typrite.Shell.AppBar;

/// <summary>
/// Drives one bar on one display: registers the AppBar, feeds shell notifications into the
/// <see cref="DisplayStateMachine"/>, and applies the resulting z-order. In P1 every full-screen app yields;
/// P2 replaces the fixed classification with <see cref="FullscreenClassifier"/> fed by a window detector.
/// </summary>
public sealed class BarCoordinator
{
    private readonly IAppBarHost _host;
    private readonly IBarSurface _surface;
    private readonly double _reservationDip;
    private readonly DisplayStateMachine _machine = new();
    private nint _hwnd;
    private PhysicalRect _monitor;
    private uint _dpi = DpiScale.BaseDpi;
    private bool _attached;

    public BarCoordinator(IAppBarHost host, IBarSurface surface, double reservationDip)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reservationDip, 0);

        _host = host;
        _surface = surface;
        _reservationDip = reservationDip;
        _machine.Transitioned += (_, e) => ModeChanged?.Invoke(this, e.To);
        _host.FullscreenAppChanged += OnFullscreenAppChanged;
        _host.PositionChanged += (_, _) => Reposition();
    }

    public event EventHandler<DisplayMode>? ModeChanged;

    public DisplayMode Mode => _machine.Mode;

    /// <summary>Rect granted by the shell on the last registration or reposition.</summary>
    public PhysicalRect Bounds { get; private set; }

    private int HeightPx => DpiScale.DipToPhysical(_reservationDip, _dpi);

    public void Attach(nint hwnd, PhysicalRect monitor, uint dpi)
    {
        _hwnd = hwnd;
        _monitor = monitor;
        _dpi = dpi;
        Register();
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        _host.Unregister();
        _attached = false;
    }

    public void DpiChanged(uint dpi)
    {
        _dpi = dpi;
        Reposition();
    }

    /// <summary>Explorer restarted (TaskbarCreated): the old registration is gone, start over.</summary>
    public void ShellRestarted()
    {
        _machine.Apply(DisplayEvent.TopologyChanged);
        Detach();
        Register();
    }

    private void Register()
    {
        Bounds = _host.Register(_hwnd, _monitor, HeightPx);
        _attached = true;
        _machine.Apply(DisplayEvent.BarVerified);
    }

    private void Reposition()
    {
        if (_attached)
        {
            Bounds = _host.Reposition(_monitor, HeightPx);
        }
    }

    private void OnFullscreenAppChanged(object? sender, bool started)
    {
        if (started)
        {
            if (_machine.Apply(DisplayEvent.FullscreenCandidateAppeared)
                && _machine.Apply(DisplayEvent.Classified(DisplayMode.YieldedFullscreen)))
            {
                _surface.SendToBottom();
            }
        }
        else if (_machine.Apply(DisplayEvent.FullscreenEnded))
        {
            _surface.BringToTop();
        }
    }
}
