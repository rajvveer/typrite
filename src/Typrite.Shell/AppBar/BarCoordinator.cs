using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;

namespace Typrite.Shell.AppBar;

/// <summary>
/// Drives one bar on one display: registers the AppBar, turns observations and shell notifications into
/// <see cref="DisplayStateMachine"/> events, and applies the resulting z-order. Without an <see cref="Observer"/>
/// every full-screen notification yields (P1 behaviour); with one, <see cref="FullscreenClassifier"/> decides.
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

    public FullscreenPolicy Policy { get; set; } = FullscreenPolicy.YieldByDefault;

    /// <summary>Produces a fresh observation of this display on demand. Null means "no detector yet": yield on every notification.</summary>
    public Func<DisplayObservation>? Observer { get; set; }

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
        Recover();
    }

    public void EnterSafeMode()
    {
        var wasOverlay = Mode == DisplayMode.CompatibleOverlay;
        if (_machine.Apply(DisplayEvent.SafeModeRequested))
        {
            if (wasOverlay)
            {
                _surface.LeaveOverlay();
            }

            _surface.SendToBottom();
        }
    }

    public void AuthorizeRecovery()
    {
        if (_machine.Apply(DisplayEvent.RecoveryAuthorized))
        {
            Recover();
        }
    }

    /// <summary>Apply a new observation of this display. Idempotent: the same observation twice changes nothing.</summary>
    public void Reevaluate(DisplayObservation o)
    {
        ArgumentNullException.ThrowIfNull(o);

        switch (Mode)
        {
            case DisplayMode.NormalReserved:
                if (o.IsProtectedOrSecureContext)
                {
                    Transition(DisplayEvent.SecureDesktopAppeared, _surface.SendToBottom);
                }
                else if (o.FullscreenCandidate is not null && _machine.Apply(DisplayEvent.FullscreenCandidateAppeared))
                {
                    Classify(o);
                }

                break;

            case DisplayMode.FullscreenDetected:
                Classify(o);
                break;

            case DisplayMode.YieldedFullscreen:
                if (o.IsProtectedOrSecureContext)
                {
                    _machine.Apply(DisplayEvent.SecureDesktopAppeared);
                }
                else if (o.FullscreenCandidate is null)
                {
                    Transition(DisplayEvent.FullscreenEnded, _surface.BringToTop);
                }

                break;

            case DisplayMode.CompatibleOverlay:
                if (o.IsProtectedOrSecureContext)
                {
                    Transition(DisplayEvent.SecureDesktopAppeared, () => { _surface.LeaveOverlay(); _surface.SendToBottom(); });
                }
                else if (o.FullscreenCandidate is null)
                {
                    Transition(DisplayEvent.FullscreenEnded, () => { _surface.LeaveOverlay(); _surface.BringToTop(); });
                }
                else if (FullscreenClassifier.Decide(o, Policy) != DisplayMode.CompatibleOverlay)
                {
                    Transition(DisplayEvent.OverlayOccluded, () => { _surface.LeaveOverlay(); _surface.SendToBottom(); });
                }

                break;

            case DisplayMode.ProtectedSurface:
                if (!o.IsProtectedOrSecureContext && _machine.Apply(DisplayEvent.DesktopRestored))
                {
                    Recover();
                }

                break;

            case DisplayMode.Recovering:
            case DisplayMode.SafeMode:
                break;
        }
    }

    private void Classify(DisplayObservation o)
    {
        var mode = FullscreenClassifier.Decide(o, Policy);
        if (!_machine.Apply(DisplayEvent.Classified(mode)))
        {
            return;
        }

        switch (mode)
        {
            case DisplayMode.YieldedFullscreen:
            case DisplayMode.ProtectedSurface:
                _surface.SendToBottom();
                break;
            case DisplayMode.CompatibleOverlay:
                _surface.EnterOverlay();
                break;
        }
    }

    private void Transition(DisplayEvent e, Action onApplied)
    {
        if (_machine.Apply(e))
        {
            onApplied();
        }
    }

    private void Register()
    {
        Bounds = _host.Register(_hwnd, _monitor, HeightPx);
        _attached = true;
        _machine.Apply(DisplayEvent.BarVerified);
    }

    private void Recover()
    {
        Detach();
        Register();
        _surface.BringToTop();
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
        if (Observer is { } observe)
        {
            Reevaluate(observe());
            return;
        }

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
