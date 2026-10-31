using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Typrite.Player.Playback;
using Typrite.Shell.AppBar;
using Typrite.Shell.Displays;
using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;
using Typrite.Shell.Win32;
using Windows.Devices.Geolocation;
using WinRT.Interop;

namespace Typrite.Desktop;

/// <summary>One bar per display. Shell logic lives in <see cref="BarCoordinator"/>, delivery in <see cref="DeviceServices"/>; this class is wiring.</summary>
public sealed partial class BarWindow : Window, IBarSurface
{
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);

    private readonly DeviceServices _services;
    private readonly nint _hwnd;
    private readonly AppBarHost _host;
    private readonly OverlappedPresenter _presenter;
    private readonly BarCoordinator _bar;
    private readonly DisplayInfo _display;
    private readonly WindowEnumerator _windows = new();
    private readonly WinEventHook _hook;
    private readonly QuietPeriodCoalescer _coalescer;
    private readonly DispatcherQueueTimer _clock;
    private readonly DispatcherQueueTimer _adTimer;
    private readonly DispatcherQueueTimer _weatherTimer;
    private readonly DispatcherQueueTimer _verificationTimer;
    private AdVisibilityTracker? _tracker;
    private bool _rotating;
    private bool _closed;
    private bool _desktopUnavailable;

    public BarWindow(DisplayArea area, DeviceServices services, FullscreenPolicy policy, bool safeMode = false, bool taskbarDemo = false)
    {
        InitializeComponent();
        _services = services;
        var config = services.Configuration;
        _host = new AppBarHost(allowActivation: taskbarDemo);
        if (taskbarDemo)
        {
            var taskbar = new DemoTaskbar();
            taskbar.ExitRequested += (_, _) => DemoExitRequested?.Invoke(this, EventArgs.Empty);
            TaskbarSlot.Child = taskbar;
        }

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

        var reservationDip = config.Layout.ReservationDip + (taskbarDemo ? DemoTaskbar.HeightDip : 0);
        _bar = new BarCoordinator(_host, this, reservationDip) { Policy = policy, Observer = Observe };
        _bar.ModeChanged += (_, mode) => _tracker?.DisplayModeChanged(mode);
        _host.ShellRestarted += (_, _) => _bar.ShellRestarted();
        _host.DpiChanged += (_, newDpi) => _bar.DpiChanged(newDpi);
        _host.DisplayTopologyChanged += (_, _) => TopologyChanged?.Invoke(this, EventArgs.Empty);
        _host.DesktopUnavailable += (_, _) =>
        {
            _desktopUnavailable = true;
            _bar.Reevaluate(Observe());
        };
        _host.DesktopRestored += (_, _) =>
        {
            _desktopUnavailable = false;
            TopologyChanged?.Invoke(this, EventArgs.Empty);
        };
        if (safeMode)
        {
            _bar.EnterSafeMode();
        }

        _bar.Attach(_hwnd, bounds, dpi);

        _coalescer = new QuietPeriodCoalescer(TimeProvider.System, QuietPeriod, MaxDelay,
            () => DispatcherQueue.TryEnqueue(() =>
            {
                if (!_closed)
                {
                    _bar.Reevaluate(Observe());
                }
            }));
        _hook = new WinEventHook();
        _hook.Changed += (_, _) => _coalescer.Signal();

        _clock = Timer(TimeSpan.FromSeconds(1), UpdateClock);
        _adTimer = Timer(TimeSpan.FromSeconds(config.Ads.DecisionIntervalSeconds), () => _ = RotateAdAsync());
        _weatherTimer = Timer(TimeSpan.FromSeconds(config.Weather.RefreshSeconds), () => _ = RefreshWeatherAsync());
        _verificationTimer = Timer(TimeSpan.FromSeconds(30), _bar.Reconcile);
        UpdateClock();
        _ = RotateAdAsync();
        _ = RefreshWeatherAsync();

        Closed += (_, _) =>
        {
            _closed = true;
            _clock.Stop();
            _adTimer.Stop();
            _weatherTimer.Stop();
            _verificationTimer.Stop();
            Ad.Stop();
            _hook.Dispose();
            _coalescer.Dispose();
            _bar.Detach();
            _host.Dispose();
        };
    }

    /// <summary>Monitors were added, removed or re-arranged; the owner rebuilds all bars.</summary>
    public event EventHandler? TopologyChanged;

    public event EventHandler? DemoExitRequested;

    public DisplayMode Mode => _bar.Mode;

    public DisplayKey DisplayKey => _display.Key;

    public void EnterSafeMode() => _bar.EnterSafeMode();

    public void AuthorizeRecovery() => _bar.AuthorizeRecovery();

    void IBarSurface.SendToBottom() => WindowZOrder.SendToBottom(_hwnd);

    void IBarSurface.BringToTop() => WindowZOrder.BringToTop(_hwnd);

    void IBarSurface.EnterOverlay() => _presenter.IsAlwaysOnTop = true;

    void IBarSurface.LeaveOverlay() => _presenter.IsAlwaysOnTop = false;

    private DispatcherQueueTimer Timer(TimeSpan interval, Action tick)
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = interval;
        t.Tick += (_, _) => tick();
        t.Start();
        return t;
    }

    private DisplayObservation Observe() =>
        FullscreenObserver.Observe(
            _display,
            _windows.Snapshot(),
            _desktopUnavailable || SecureDesktopProbe.IsSecureDesktopActive(),
            _bar.Policy,
            _ => OverlayProbe.IsBarVisibleAt(_hwnd, _bar.Bounds));

    /// <summary>Asks for the next creative only while the bar can be seen; a hidden bar keeps what it has and claims nothing.</summary>
    private async Task RotateAdAsync()
    {
        if (_closed || _rotating || Mode is not (DisplayMode.NormalReserved or DisplayMode.CompatibleOverlay))
        {
            return;
        }

        _rotating = true;
        try
        {
            var resolved = await _services.Pipeline.NextAsync(_display.Key.Value, CancellationToken.None);
            if (_closed)
            {
                return;
            }

            var ctx = new PlaybackContext(
                _services.DeviceId, _services.SessionId, _display.Key.Value,
                resolved.Decision?.DecisionId, resolved.Manifest.CreativeId,
                _services.Configuration.ConfigurationVersion, resolved.Manifest.DurationMs);
            var tracker = new AdVisibilityTracker(ctx, TimeProvider.System, _services.Record);
            tracker.DisplayModeChanged(Mode);
            _tracker = tracker;
            await Ad.ShowAsync(resolved, tracker);
        }
        finally
        {
            _rotating = false;
        }
    }

    private async Task RefreshWeatherAsync()
    {
        var (lat, lon) = await TryLocateAsync();
        var report = await _services.GetWeatherAsync(lat, lon, CancellationToken.None);
        if (_closed || report is null)
        {
            return;
        }

        WeatherText.Text = string.Create(CultureInfo.CurrentCulture, $"{Math.Round(report.TemperatureC)}°C · {report.Condition}");
        LocationText.Text = report.LocationLabel;
    }

    /// <summary>Coarse location only, and only when the user has allowed desktop apps to use it.</summary>
    private static async Task<(double?, double?)> TryLocateAsync()
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            {
                return (null, null);
            }

            var locator = new Geolocator { DesiredAccuracyInMeters = 5000 };
            var pos = await locator.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10));
            return (pos.Coordinate.Point.Position.Latitude, pos.Coordinate.Point.Position.Longitude);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or TimeoutException or System.Runtime.InteropServices.COMException)
        {
            return (null, null);
        }
    }

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
