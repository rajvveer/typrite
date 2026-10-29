using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Typrite.Shell.AppBar;
using Typrite.Shell.Displays;
using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;
using Typrite.Shell.Win32;
using WinRT.Interop;

namespace Typrite.Desktop;

/// <summary>One bar per display. All shell logic lives in <see cref="BarCoordinator"/>; this class is wiring only.</summary>
public sealed partial class BarWindow : Window, IBarSurface
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);

    private readonly nint _hwnd;
    private readonly AppBarHost _host = new();
    private readonly OverlappedPresenter _presenter;
    private readonly BarCoordinator _bar;
    private readonly DisplayInfo _display;
    private readonly WindowEnumerator _windows = new();
    private readonly WinEventHook _hook;
    private readonly QuietPeriodCoalescer _coalescer;
    private readonly DispatcherQueueTimer _clock;

    public BarWindow(DisplayArea area, double reservationDip, FullscreenPolicy policy)
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        _presenter.IsResizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsAlwaysOnTop = false;
        AppWindow.SetPresenter(_presenter);
        AppWindow.IsShownInSwitchers = false;

        var b = area.OuterBounds;
        var bounds = new PhysicalRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
        var dpi = WindowZOrder.DpiOf(_hwnd);
        _display = ResolveDisplay(area, bounds, dpi);

        _bar = new BarCoordinator(_host, this, reservationDip) { Policy = policy, Observer = Observe };
        _host.ShellRestarted += (_, _) => _bar.ShellRestarted();
        _host.DpiChanged += (_, newDpi) => _bar.DpiChanged(newDpi);
        _host.DisplayTopologyChanged += (_, _) => TopologyChanged?.Invoke(this, EventArgs.Empty);
        _bar.Attach(_hwnd, bounds, dpi);

        _coalescer = new QuietPeriodCoalescer(TimeProvider.System, QuietPeriod, MaxDelay,
            () => DispatcherQueue.TryEnqueue(() => _bar.Reevaluate(Observe())));
        _hook = new WinEventHook();
        _hook.Changed += (_, _) => _coalescer.Signal();

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();

        Closed += (_, _) =>
        {
            _clock.Stop();
            _hook.Dispose();
            _coalescer.Dispose();
            _bar.Detach();
            _host.Dispose();
        };
    }

    /// <summary>Monitors were added, removed or re-arranged; the owner rebuilds all bars.</summary>
    public event EventHandler? TopologyChanged;

    public DisplayMode Mode => _bar.Mode;

    public DisplayKey DisplayKey => _display.Key;

    void IBarSurface.SendToBottom() => WindowZOrder.SendToBottom(_hwnd);

    void IBarSurface.BringToTop() => WindowZOrder.BringToTop(_hwnd);

    void IBarSurface.EnterOverlay() => _presenter.IsAlwaysOnTop = true;

    void IBarSurface.LeaveOverlay() => _presenter.IsAlwaysOnTop = false;

    private DisplayObservation Observe() =>
        FullscreenObserver.Observe(
            _display,
            _windows.Snapshot(),
            SecureDesktopProbe.IsSecureDesktopActive(),
            _bar.Policy,
            _ => OverlayProbe.IsBarVisibleAt(_hwnd, _bar.Bounds));

    private static DisplayInfo ResolveDisplay(DisplayArea area, PhysicalRect bounds, uint dpi)
    {
        foreach (var d in DisplayEnumerator.Enumerate())
        {
            if (d.Bounds == bounds)
            {
                return d;
            }
        }

        return new DisplayInfo(DisplayKey.From($"displayarea:{area.DisplayId.Value}", null), bounds, dpi);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("t", CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("D", CultureInfo.CurrentCulture);
    }
}
