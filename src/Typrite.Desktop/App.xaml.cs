using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Typrite.Shell.Fullscreen;

namespace Typrite.Desktop;

public partial class App : Application
{
    /// <summary>Example value from the architecture guide; not a client-approved budget.</summary>
    private const double ReservationDip = 210;

    private readonly List<BarWindow> _bars = [];
    private Mutex? _singleInstance;
    private DispatcherQueueTimer? _rebuildTimer;
    private bool _rebuilding;

    /// <summary>Overlay allowlist. Empty until an app has passed the physical overlay test; later fed from device policy.</summary>
    private FullscreenPolicy _policy = FullscreenPolicy.YieldByDefault;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Typrite.Desktop", out var createdNew);
        if (!createdNew)
        {
            Exit();
            return;
        }

        _rebuildTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _rebuildTimer.Interval = TimeSpan.FromMilliseconds(500);
        _rebuildTimer.IsRepeating = false;
        _rebuildTimer.Tick += (_, _) => RebuildBars();

        CreateBars();
    }

    private void CreateBars()
    {
        foreach (var area in DisplayArea.FindAll())
        {
            var bar = new BarWindow(area, ReservationDip, _policy);
            bar.TopologyChanged += (_, _) => _rebuildTimer?.Start();
            bar.Closed += (_, _) =>
            {
                _bars.Remove(bar);
                if (_bars.Count == 0 && !_rebuilding)
                {
                    Exit();
                }
            };
            _bars.Add(bar);
            bar.AppWindow.Show(activateWindow: false);
        }
    }

    /// <summary>WM_DISPLAYCHANGE arrives per bar and in bursts; one debounced rebuild recreates every bar from the new topology.</summary>
    private void RebuildBars()
    {
        _rebuilding = true;
        try
        {
            foreach (var bar in _bars.ToArray())
            {
                bar.Close();
            }

            _bars.Clear();
            CreateBars();
        }
        finally
        {
            _rebuilding = false;
        }
    }
}
