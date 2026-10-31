using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Typrite.Shell.Fullscreen;

namespace Typrite.Desktop;

public partial class App : Application
{
    private readonly List<BarWindow> _bars = [];
    private DeviceServices? _services;
    private Mutex? _singleInstance;
    private DispatcherQueueTimer? _rebuildTimer;
    private DispatcherQueueTimer? _commandTimer;
    private bool _rebuilding;
    private bool _safeMode;
    private bool _taskbarDemo;

    /// <summary>Overlay allowlist. Empty until an app has passed the physical overlay test; later fed from device policy.</summary>
    private FullscreenPolicy _policy = FullscreenPolicy.YieldByDefault;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _taskbarDemo = Environment.GetCommandLineArgs().Contains("--taskbar-demo", StringComparer.Ordinal);
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

        _services = new DeviceServices(DesktopSettings.Load(), DispatcherQueue.GetForCurrentThread());
        CreateBars();
        _ = ConnectAsync(_services);

        _commandTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _commandTimer.Interval = TimeSpan.FromSeconds(10);
        _commandTimer.Tick += async (_, _) => await ApplyAgentCommandsAsync();
        _commandTimer.Start();
    }

    /// <summary>Commands arrive through the agent, which already authenticated and audited them server-side.</summary>
    private async Task ApplyAgentCommandsAsync()
    {
        if (_services is null)
        {
            return;
        }

        while (await _services.TryNextAgentCommandAsync(CancellationToken.None) is { } command)
        {
            switch (command)
            {
                case "enter-safe-mode":
                    _safeMode = true;
                    _bars.ForEach(b => b.EnterSafeMode());
                    break;
                case "leave-safe-mode":
                    _safeMode = false;
                    _bars.ForEach(b => b.AuthorizeRecovery());
                    break;
                case "restart-bar":
                    RebuildBars();
                    break;
                case "refresh-configuration":
                    await _services.RefreshConfigurationAsync(CancellationToken.None);
                    break;
            }
        }
    }

    /// <summary>Bars render from cached state immediately; registration and policy arrive in the background.</summary>
    private static async Task ConnectAsync(DeviceServices services)
    {
        try
        {
            await services.RegisterDevAsync(CancellationToken.None);
            await services.RefreshConfigurationAsync(CancellationToken.None);
        }
        catch (HttpRequestException)
        {
            // Offline at start: keep the cached configuration and retry on the next upload tick.
        }
    }

    private void CreateBars()
    {
        foreach (var area in DisplayArea.FindAll())
        {
            var bar = new BarWindow(area, _services!, _policy, _safeMode, _taskbarDemo);
            bar.DemoExitRequested += (_, _) =>
            {
                foreach (var openBar in _bars.ToArray())
                {
                    openBar.Close();
                }
            };
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
