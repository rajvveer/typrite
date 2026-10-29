using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Typrite.Shell.AppBar;
using Typrite.Shell.Fullscreen;
using Typrite.Shell.Geometry;
using Typrite.Shell.Win32;
using WinRT.Interop;

namespace Typrite.Desktop;

/// <summary>One bar per display. All shell logic lives in <see cref="BarCoordinator"/>; this class is wiring only.</summary>
public sealed partial class BarWindow : Window, IBarSurface
{
    private readonly nint _hwnd;
    private readonly AppBarHost _host = new();
    private readonly BarCoordinator _bar;
    private readonly DispatcherQueueTimer _clock;

    public BarWindow(DisplayArea area, double reservationDip)
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        _bar = new BarCoordinator(_host, this, reservationDip);
        _host.ShellRestarted += (_, _) => _bar.ShellRestarted();
        _host.DpiChanged += (_, dpi) => _bar.DpiChanged(dpi);

        var b = area.OuterBounds;
        _bar.Attach(_hwnd, new PhysicalRect(b.X, b.Y, b.X + b.Width, b.Y + b.Height), WindowZOrder.DpiOf(_hwnd));

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();

        Closed += (_, _) =>
        {
            _clock.Stop();
            _bar.Detach();
            _host.Dispose();
        };
    }

    public DisplayMode Mode => _bar.Mode;

    void IBarSurface.SendToBottom() => WindowZOrder.SendToBottom(_hwnd);

    void IBarSurface.BringToTop() => WindowZOrder.BringToTop(_hwnd);

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("t", CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("D", CultureInfo.CurrentCulture);
    }
}
