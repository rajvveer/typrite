using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Typrite.Shell.Fullscreen;
using Typrite.Shell.Taskbar;
using Typrite.Shell.Win32;
using Windows.UI.ViewManagement;

namespace Typrite.Desktop;

/// <summary>Interactive demo dock with live window state and lightweight composition motion.</summary>
public sealed partial class DemoTaskbar : UserControl
{
    public const double HeightDip = 58;
    private readonly WindowEnumerator _windows = new();
    private readonly DispatcherQueueTimer _stateTimer;
    private readonly UISettings _uiSettings = new();
    private readonly Button[] _dockButtons;
    private readonly DockApp[] _pinned;
    private IReadOnlyList<WindowObservation> _openWindows = [];
    private bool _refreshing;
    private int _refreshCount;
    private nint _lastForeground;
    private Button? _hovered;
    private Button? _focused;
    private readonly Dictionary<Button, (float Scale, float Lift)> _motionTargets = [];

    public DemoTaskbar()
    {
        InitializeComponent();
        _dockButtons = [StartButton, AppsButton, ExplorerButton, EdgeButton, NotepadButton, CalculatorButton];
        _pinned = [
            new(ExplorerButton, ExplorerIndicator, "File Explorer", "explorer.exe", ["explorer.exe"]),
            new(EdgeButton, EdgeIndicator, "Microsoft Edge", "microsoft-edge:", ["msedge.exe"]),
            new(NotepadButton, NotepadIndicator, "Notepad", "notepad.exe", ["notepad.exe"]),
            new(CalculatorButton, CalculatorIndicator, "Calculator", "calculator:", ["calculatorapp.exe", "calculator.exe"]),
        ];
        foreach (var button in _dockButtons)
        {
            button.PointerEntered += OnPointerEntered;
            button.PointerExited += OnPointerExited;
            button.GotFocus += OnDockFocus;
            button.LostFocus += OnDockBlur;
            button.AddHandler(PointerPressedEvent, new PointerEventHandler(OnDockPressed), true);
            button.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnDockReleased), true);
            button.PointerCanceled += OnPointerExited;
        }
        _stateTimer = DispatcherQueue.CreateTimer();
        _stateTimer.Interval = TimeSpan.FromSeconds(2);
        _stateTimer.Tick += async (_, _) => await UpdateWindowStateAsync();
        Loaded += async (_, _) => { _stateTimer.Start(); await UpdateWindowStateAsync(); };
        Unloaded += (_, _) => { _stateTimer.Stop(); _hovered = _focused = null; UpdateMotion(); };
    }

    public event EventHandler? ExitRequested;

    public void SetPalette(Windows.UI.Color background, Windows.UI.Color border, Windows.UI.Color accent)
    {
        DockFrame.Background = new SolidColorBrush(background);
        DockFrame.BorderBrush = new SolidColorBrush(border);
        ((SolidColorBrush)Resources["DockAccent"]).Color = accent;
        var gradient = (LinearGradientBrush)DockIsland.Background;
        gradient.GradientStops[0].Color = Mix(background, accent, .15);
        gradient.GradientStops[1].Color = Mix(background, accent, .05);
        var outline = (LinearGradientBrush)DockIsland.BorderBrush;
        outline.GradientStops[0].Color = Mix(background, accent, .32);
        outline.GradientStops[1].Color = Mix(background, accent, .12);
    }

    private static Windows.UI.Color Mix(Windows.UI.Color background, Windows.UI.Color accent, double amount) =>
        Windows.UI.Color.FromArgb(255,
            (byte)(background.R + (accent.R - background.R) * amount),
            (byte)(background.G + (accent.G - background.G) * amount),
            (byte)(background.B + (accent.B - background.B) * amount));

    private void OnDockSizeChanged(object sender, SizeChangedEventArgs e)
    {
        BrandPanel.Visibility = e.NewSize.Width < 850 ? Visibility.Collapsed : Visibility.Visible;
        ExitButton.Visibility = e.NewSize.Width < 650 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse) return;
        _hovered = (Button)sender;
        UpdateMotion();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(_hovered, sender)) _hovered = null;
        UpdateMotion();
    }

    private void OnDockFocus(object sender, RoutedEventArgs e)
    {
        _focused = ((Button)sender).FocusState == FocusState.Keyboard ? (Button)sender : null;
        UpdateMotion();
    }

    private void OnDockBlur(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(_focused, sender)) _focused = null;
        UpdateMotion();
    }

    private void OnDockPressed(object sender, PointerRoutedEventArgs e) => AnimateIcon((Button)sender, .94f, 0);
    private void OnDockReleased(object sender, PointerRoutedEventArgs e) => UpdateMotion();

    private void UpdateMotion()
    {
        var selected = Array.IndexOf(_dockButtons, _hovered ?? _focused);
        for (var i = 0; i < _dockButtons.Length; i++)
        {
            var distance = selected < 0 ? int.MaxValue : Math.Abs(i - selected);
            AnimateIcon(_dockButtons[i], distance == 0 ? 1.17f : distance == 1 ? 1.05f : 1f,
                distance == 0 ? -3 : distance == 1 ? -1 : 0);
        }
    }

    private void AnimateIcon(Button button, float scale, float lift)
    {
        if (button.Content is not Grid content || content.Children[0] is not FrameworkElement icon) return;
        if (!_uiSettings.AnimationsEnabled) { scale = 1; lift = 0; }
        if (_motionTargets.TryGetValue(button, out var previous) && previous == (scale, lift)) return;
        _motionTargets[button] = (scale, lift);
        var visual = ElementCompositionPreview.GetElementVisual(icon);
        ElementCompositionPreview.SetIsTranslationEnabled(icon, true);
        visual.CenterPoint = new Vector3((float)icon.ActualWidth / 2, (float)icon.ActualHeight / 2, 0);
        var size = visual.Compositor.CreateVector3KeyFrameAnimation();
        size.Duration = TimeSpan.FromMilliseconds(180);
        size.InsertKeyFrame(1, new Vector3(scale, scale, 1));
        visual.StartAnimation("Scale", size);
        var move = visual.Compositor.CreateVector3KeyFrameAnimation();
        move.Duration = TimeSpan.FromMilliseconds(180);
        move.InsertKeyFrame(1, new Vector3(0, lift, 0));
        visual.StartAnimation("Translation", move);
    }

    private async Task UpdateWindowStateAsync()
    {
        if (_refreshing || !IsLoaded) return;
        _refreshing = true;
        try
        {
            // Identity and signature lookup can touch disk. Keep it off the UI thread.
            var clearCache = _refreshCount++ % 30 == 0;
            var snapshot = await Task.Run(() =>
            {
                if (clearCache) _windows.TrimCaches();
                return TaskbarWindows.Select(_windows.Snapshot()).ToArray();
            });
            if (!IsLoaded) return;
            _openWindows = snapshot;
            var foreground = TaskbarActions.ForegroundWindow;
            if (snapshot.Any(window => window.Handle == foreground)) _lastForeground = foreground;
            RunningCount.Text = snapshot.Length > 99 ? "99+" : snapshot.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ToolTipService.SetToolTip(AppsButton, $"Running apps · {snapshot.Length} windows");
            foreach (var app in _pinned)
            {
                var matches = snapshot.Where(app.Matches).ToArray();
                var active = matches.Any(window => window.Handle == _lastForeground && !window.IsMinimized);
                app.Indicator.Opacity = matches.Length == 0 ? 0 : active ? 1 : .55;
                app.Indicator.Width = active ? 16 : 4;
                app.Button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(active ? (byte)22 : (byte)0, 187, 214, 255));
                var state = matches.Length == 0 ? "Open" : active ? "Active" : $"{matches.Length} open";
                ToolTipService.SetToolTip(app.Button, $"{app.Name} · {state}");
                AutomationProperties.SetHelpText(app.Button, state);
            }
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            StatusText.Text = "App list unavailable";
        }
        finally { _refreshing = false; }
    }

    private async void LaunchApp(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target }) return;
        StartFlyout.Hide();
        var pinned = _pinned.FirstOrDefault(app => app.Target == target);
        try
        {
            var existing = pinned is null ? null : _openWindows.FirstOrDefault(pinned.Matches);
            if (existing is not null)
            {
                if (!TaskbarActions.Activate(existing.Handle))
                {
                    StatusText.Text = "Could not switch app";
                    await UpdateWindowStateAsync();
                    return;
                }
                _lastForeground = existing.Handle;
            }
            else
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            StatusText.Text = "Workspace";
            ToolTipService.SetToolTip(StatusText, null);
            await UpdateWindowStateAsync();
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            StatusText.Text = "Could not open app";
            ToolTipService.SetToolTip(StatusText, error.Message);
        }
    }

    private void RefreshRunningApps(object sender, object e)
    {
        RunningApps.Items.Clear();
        foreach (var window in _openWindows)
        {
            var title = TaskbarActions.Title(window.Handle);
            if (string.IsNullOrWhiteSpace(title)) continue;
            var item = new MenuFlyoutItem { Text = title };
            if (window.Handle == _lastForeground) item.Icon = new FontIcon { Glyph = "\uE73E" };
            item.Click += (_, _) =>
            {
                if (TaskbarActions.Activate(window.Handle)) _lastForeground = window.Handle;
                else StatusText.Text = "Could not switch app";
            };
            RunningApps.Items.Add(item);
        }
        if (RunningApps.Items.Count == 0)
            RunningApps.Items.Add(new MenuFlyoutItem { Text = "No running apps", IsEnabled = false });
    }

    private void ExitDemo(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private sealed record DockApp(Button Button, Border Indicator, string Name, string Target, string[] Executables)
    {
        public bool Matches(WindowObservation window) => window.App is not null &&
            Executables.Contains(System.IO.Path.GetFileName(window.App.ExecutablePath), StringComparer.OrdinalIgnoreCase);
    }
}
