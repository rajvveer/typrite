using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    private readonly bool _taskbarDemo;
    private readonly CancellationTokenSource _weatherLifetime = new();
    private WeatherCity? _weatherCity;
    private bool _automaticCity;
    private bool _weatherRefreshing;
    private DateTimeOffset _locatedAt;
    private DemoWeather? _latestWeather;
    private string? _themePeriod;
    private DemoTaskbar? _demoNavigation;

    public BarWindow(DisplayArea area, DeviceServices services, FullscreenPolicy policy, bool safeMode = false, bool taskbarDemo = false)
    {
        InitializeComponent();
        Title = "Typrite";
        _services = services;
        _taskbarDemo = taskbarDemo;
        Ad.EnableMockCampaign(taskbarDemo);
        _weatherCity = services.DemoWeather.LoadCity();
        _automaticCity = _weatherCity is null;
        CityButton.IsEnabled = taskbarDemo;
        var config = services.Configuration;
        var inspectUi = Environment.GetCommandLineArgs().Contains("--inspect-ui", StringComparer.Ordinal);
        _host = new AppBarHost(allowActivation: taskbarDemo, showInSwitchers: inspectUi);
        if (taskbarDemo)
        {
            var taskbar = new DemoTaskbar();
            _demoNavigation = taskbar;
            taskbar.ExitRequested += (_, _) => DemoExitRequested?.Invoke(this, EventArgs.Empty);
            TaskbarSlot.Child = taskbar;
        }

        _hwnd = WindowNative.GetWindowHandle(this);
        WindowChrome.MakeFlush(_hwnd);
        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        _presenter.IsResizable = false;
        _presenter.IsMinimizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsAlwaysOnTop = false;
        AppWindow.SetPresenter(_presenter);
        AppWindow.IsShownInSwitchers = inspectUi;

        var b = area.OuterBounds;
        var bounds = new PhysicalRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height);
        var dpi = WindowZOrder.DpiOf(_hwnd);
        _display = ResolveDisplay(area, bounds, dpi);

        var reservationDip = (taskbarDemo ? 176 : config.Layout.ReservationDip) + (taskbarDemo ? DemoTaskbar.HeightDip : 0);
        _bar = new BarCoordinator(_host, this, reservationDip) { Policy = policy, Observer = Observe };
        _bar.ModeChanged += (_, mode) =>
        {
            _tracker?.DisplayModeChanged(mode);
            Ad.SetPlaybackVisible(mode is DisplayMode.NormalReserved or DisplayMode.CompatibleOverlay);
        };
        _host.ShellRestarted += (_, _) =>
        {
            ShellRestarted?.Invoke(this, EventArgs.Empty);
            _bar.ShellRestarted();
        };
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
            _weatherLifetime.Cancel();
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

    public event EventHandler? ShellRestarted;

    public DisplayMode Mode => _bar.Mode;

    public DisplayKey DisplayKey => _display.Key;

    public void EnterSafeMode() => _bar.EnterSafeMode();

    public void AuthorizeRecovery() => _bar.AuthorizeRecovery();

    /// <summary>Registration finished after the bar was built; fetch what the unauthenticated first attempt could not.</summary>
    public void RefreshContent()
    {
        _ = RotateAdAsync();
        _ = RefreshWeatherAsync();
    }

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

    private async Task RefreshWeatherAsync(bool force = false)
    {
        if (_closed || _weatherRefreshing) return;
        _weatherRefreshing = true;
        WeatherRefreshButton.IsEnabled = false;
        CityButton.IsEnabled = false;
        WeatherUpdatedText.Text = "Updating…";
        try
        {
            if (_taskbarDemo)
            {
                if (_automaticCity && (_weatherCity is null || DateTimeOffset.UtcNow - _locatedAt > TimeSpan.FromMinutes(30)))
                {
                    var (lat, lon) = await TryLocateAsync();
                    if (lat is { } latitude && lon is { } longitude)
                    {
                        _weatherCity = await _services.DemoWeather.CityFromDeviceAsync(latitude, longitude, _weatherLifetime.Token);
                        ToolTipService.SetToolTip(CityButton, "Automatic · device location. Click to change.");
                    }
                    else
                    {
                        _weatherCity = await _services.DemoWeather.DetectCityAsync(_weatherLifetime.Token) ?? _weatherCity;
                        ToolTipService.SetToolTip(CityButton, "Automatic · approximate network location. Click to change.");
                    }
                    _locatedAt = DateTimeOffset.UtcNow;
                }
                if (_closed) return;
                if (_weatherCity is null)
                {
                    LocationText.Text = "Choose your city";
                    WeatherText.Text = "Location unavailable";
                    FeelsLikeText.Text = "Tap the city name to set it";
                    WeatherUpdatedText.Text = "Tap refresh to retry";
                    return;
                }
                LocationText.Text = _weatherCity.Label;
                var result = await _services.DemoWeather.GetAsync(_weatherCity, force, _weatherLifetime.Token);
                if (_closed) return;
                if (result.Weather is not { } weather)
                {
                    WeatherText.Text = "Weather unavailable";
                    FeelsLikeText.Text = "Check your connection";
                    WeatherUpdatedText.Text = result.Error;
                    return;
                }
                TemperatureText.Text = Math.Round(weather.TemperatureC).ToString(CultureInfo.CurrentCulture);
                WeatherText.Text = weather.Condition;
                WeatherGlyph.Text = weather.Icon;
                _latestWeather = weather;
                NightArt.Visibility = weather.Code <= 2 && !weather.IsDay ? Visibility.Visible : Visibility.Collapsed;
                SunArt.Visibility = weather.Code <= 2 && weather.IsDay ? Visibility.Visible : Visibility.Collapsed;
                CloudArt.Visibility = weather.Code > 2 ? Visibility.Visible : Visibility.Collapsed;
                RainArt.Visibility = weather.Code >= 51 ? Visibility.Visible : Visibility.Collapsed;
                UpdateForecastLayout();
                FeelsLikeText.Text = string.Create(CultureInfo.CurrentCulture, $"Feels like {weather.FeelsLikeC:0}°");
                HighLowText.Text = string.Create(CultureInfo.CurrentCulture, $"H {weather.HighC:0}°   L {weather.LowC:0}°");
                WindText.Text = string.Create(CultureInfo.CurrentCulture, $"Wind {weather.WindKmh:0} km/h");
                HumidityText.Text = string.Create(CultureInfo.CurrentCulture, $"Humidity {weather.Humidity:0}%");
                WeatherUpdatedText.Text = result.IsStale ? "Offline · last update" : "Open-Meteo";
                ToolTipService.SetToolTip(WeatherUpdatedText, $"Weather by Open-Meteo · updated {weather.ObservedAtUtc.ToLocalTime():ddd HH:mm}");
            }
            else
            {
                var (lat, lon) = await TryLocateAsync();
                var report = await _services.GetWeatherAsync(lat, lon, _weatherLifetime.Token);
                if (_closed) return;
                if (report is null) throw new HttpRequestException("No weather report.");
                TemperatureText.Text = Math.Round(report.TemperatureC).ToString(CultureInfo.CurrentCulture);
                WeatherText.Text = report.Condition;
                WeatherGlyph.Text = WeatherEmoji(report.Condition);
                LocationText.Text = report.LocationLabel;
                FeelsLikeText.Text = "Current conditions";
                WeatherUpdatedText.Text = $"Updated {report.ObservedAtUtc.ToLocalTime():t}";
            }
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        {
            if (!_closed)
            {
                WeatherText.Text = "Waiting for connection";
                FeelsLikeText.Text = "Tap refresh to try again";
                WeatherUpdatedText.Text = "Offline";
            }
        }
        finally
        {
            _weatherRefreshing = false;
            if (!_closed)
            {
                WeatherRefreshButton.IsEnabled = true;
                CityButton.IsEnabled = _taskbarDemo;
            }
        }
    }

    private async void RefreshWeatherClicked(object sender, RoutedEventArgs e) => await RefreshWeatherAsync(force: true);

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ClockColumn is null || ForecastCard is null) return;
        var wide = e.NewSize.Width >= 1150;
        var clockWidth = e.NewSize.Width >= 1550 ? 190 : 155;
        var forecastWidth = !wide ? 0 : e.NewSize.Width >= 1550 ? 350 : 225;
        var adWidth = Math.Clamp(e.NewSize.Width * 0.34, 340, 660);
        ClockColumn.Width = new GridLength(clockWidth);
        WeatherColumn.Width = new GridLength(1, GridUnitType.Star);
        ForecastColumn.Width = new GridLength(forecastWidth);
        AdColumn.Width = new GridLength(adWidth);
        ForecastCard.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        WeatherCurrentGrid.Width = wide ? Math.Max(250, e.NewSize.Width - 64 - clockWidth - forecastWidth - adWidth - 34) : double.NaN;
        WeatherCurrentGrid.HorizontalAlignment = wide ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        UpdateForecastLayout();
    }

    private void OnForecastSizeChanged(object sender, SizeChangedEventArgs e) => UpdateForecastLayout();

    private void UpdateForecastLayout()
    {
        if (HourlyForecast is null || ForecastCard is null || ForecastEmptyText is null) return;
        var available = Math.Max(0, ForecastCard.ActualWidth - 28);
        var count = Math.Clamp((int)(available / 60), 2, 4);
        var hours = _latestWeather?.Hours.Where(hour => hour.Time > DateTimeOffset.UtcNow).Take(count).ToArray() ?? [];
        var itemWidth = hours.Length > 0 ? available / hours.Length : 70;
        var style = new Style(typeof(ContentPresenter));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, itemWidth));
        HourlyForecast.ItemContainerStyle = style;
        HourlyForecast.ItemsSource = hours;
        ForecastEmptyText.Visibility = hours.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (hours.Length > 0)
        {
            var change = hours[^1].TemperatureC - hours[0].TemperatureC;
            ForecastTrendText.Text = change < -0.5 ? "Cooling down" : change > 0.5 ? "Warming up" : "Steady conditions";
        }
    }

    private async void SearchWeatherCity(object sender, RoutedEventArgs e)
    {
        CitySearchButton.IsEnabled = false;
        CitySearchStatus.Text = "Searching…";
        try
        {
            var cities = await _services.DemoWeather.SearchAsync(CitySearch.Text, _weatherLifetime.Token);
            if (_closed) return;
            CityResults.ItemsSource = cities;
            CitySearchStatus.Text = cities.Count == 0 ? "No matching city. Try city, country." : "Choose a city below.";
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (!_closed) CitySearchStatus.Text = "Search unavailable. Please try again.";
        }
        finally { if (!_closed) CitySearchButton.IsEnabled = true; }
    }

    private async void ChooseWeatherCity(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WeatherCity city || _weatherRefreshing) return;
        try { _services.DemoWeather.SaveCity(city); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            CitySearchStatus.Text = "Couldn't save this city. Please try again.";
            return;
        }
        _automaticCity = false;
        _weatherCity = city;
        ToolTipService.SetToolTip(CityButton, "Selected city. Click to use automatic location.");
        CityFlyout.Hide();
        await RefreshWeatherAsync(force: true);
    }

    private async void UseAutomaticLocation(object sender, RoutedEventArgs e)
    {
        if (_weatherRefreshing) return;
        try { _services.DemoWeather.UseAutomaticCity(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            CitySearchStatus.Text = "Couldn't save your preference. Please try again.";
            return;
        }
        _automaticCity = true;
        _locatedAt = default;
        CityFlyout.Hide();
        await RefreshWeatherAsync(force: true);
    }

    /// <summary>Conditions as named by the backend's weather providers.</summary>
    private static string WeatherEmoji(string condition) => condition switch
    {
        "Clear" => "☀️",
        "Partly cloudy" => "⛅",
        "Fog" => "🌫️",
        "Drizzle" or "Rain" or "Light rain" or "Showers" => "🌧️",
        "Snow" or "Snow showers" => "❄️",
        "Thunderstorm" => "⛈️",
        "Windy" => "💨",
        _ => "☁️",
    };

    /// <summary>Coarse location only, and only when the user has allowed desktop apps to use it.</summary>
    private static async Task<(double?, double?)> TryLocateAsync()
    {
        try
        {
            var locator = new Geolocator { DesiredAccuracyInMeters = 10000 };
            if (locator.LocationStatus == PositionStatus.Disabled) return (null, null);
            var pos = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(3));
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
        UpdateTimePalette(now.Hour);
        var culture = CultureInfo.CurrentCulture;
        var pattern = culture.DateTimeFormat.ShortTimePattern;

        // The AM/PM designator renders smaller beside the time; 24-hour cultures have none.
        var hasDesignator = pattern.Contains('t', StringComparison.Ordinal);
        ClockText.Text = now.ToString(hasDesignator ? pattern.Replace("t", string.Empty, StringComparison.Ordinal).Trim() : pattern, culture);
        MeridiemText.Text = hasDesignator ? now.ToString("tt", culture) : string.Empty;
        DateText.Text = now.ToString("ddd · dd MMM", culture).ToUpper(culture);
        var offset = TimeZoneInfo.Local.GetUtcOffset(now);
        GreetingText.Text = $"LOCAL TIME  ·  UTC{(offset < TimeSpan.Zero ? "−" : "+")}{offset.Duration():hh\\:mm}";
    }

    private void UpdateTimePalette(int hour)
    {
        var theme = hour switch
        {
            >= 5 and < 10 => (Name: "MORNING", Root: 0xFF17140Fu, Start: 0xFF493C28u, End: 0xFF29231Bu, Border: 0xFF665239u, Accent: 0xFFFFD49Bu, Muted: 0xFFC3AF93u),
            >= 10 and < 17 => (Name: "THIS AFTERNOON", Root: 0xFF10191Fu, Start: 0xFF244559u, End: 0xFF172C3Au, Border: 0xFF3B6077u, Accent: 0xFFB9E9FFu, Muted: 0xFF9BBCCDu),
            >= 17 and < 20 => (Name: "THIS EVENING", Root: 0xFF19131Fu, Start: 0xFF432C53u, End: 0xFF281E38u, Border: 0xFF644775u, Accent: 0xFFE6BEF5u, Muted: 0xFFB9A1CBu),
            _ => (Name: "TONIGHT", Root: 0xFF0B111Du, Start: 0xFF1B2E50u, End: 0xFF111D34u, Border: 0xFF2C456Cu, Accent: 0xFFBDD6FFu, Muted: 0xFF91A8CBu),
        };
        if (_themePeriod == theme.Name) return;
        _themePeriod = theme.Name;
        MainSurface.Background = new SolidColorBrush(Color(theme.Root));
        ((SolidColorBrush)MainSurface.Resources["TimeAccent"]).Color = Color(theme.Accent);
        ((SolidColorBrush)MainSurface.Resources["TimeMuted"]).Color = Color(theme.Muted);
        ((SolidColorBrush)MainSurface.Resources["TimeOutline"]).Color = Color(theme.Border);
        var gradient = (LinearGradientBrush)MainSurface.Resources["TimeGradient"];
        gradient.GradientStops[0].Color = Color(theme.Start);
        gradient.GradientStops[1].Color = Color(theme.End);
        ForecastPeriodText.Text = theme.Name;
        _demoNavigation?.SetPalette(Color(theme.Root), Color(theme.Border), Color(theme.Accent));
    }

    private static Windows.UI.Color Color(uint value) => Windows.UI.Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
}
