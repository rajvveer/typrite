using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Typrite.Shell.Taskbar;
using Typrite.Shell.Win32;

namespace Typrite.Desktop;

/// <summary>Interactive Windows-style demo navigation. It is not a shell or a security boundary.</summary>
public sealed partial class DemoTaskbar : UserControl
{
    public const double HeightDip = 48;
    private readonly WindowEnumerator _windows = new();
    private readonly DispatcherQueueTimer _clock;

    public DemoTaskbar()
    {
        InitializeComponent();
        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => UpdateClock();
        Loaded += (_, _) => { UpdateClock(); _clock.Start(); };
        Unloaded += (_, _) => _clock.Stop();
    }

    public event EventHandler? ExitRequested;

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("t", CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("d", CultureInfo.CurrentCulture);
    }

    private void LaunchApp(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            StatusText.Text = "Demo · unlocked";
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
        _windows.TrimCaches();
        foreach (var window in TaskbarWindows.Select(_windows.Snapshot()))
        {
            var title = TaskbarActions.Title(window.Handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var item = new MenuFlyoutItem { Text = title };
            item.Click += (_, _) =>
            {
                if (!TaskbarActions.Activate(window.Handle))
                {
                    StatusText.Text = "Could not switch app";
                }
            };
            RunningApps.Items.Add(item);
        }

        if (RunningApps.Items.Count == 0)
        {
            RunningApps.Items.Add(new MenuFlyoutItem { Text = "No running apps", IsEnabled = false });
        }
    }

    private void ExitDemo(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
}
