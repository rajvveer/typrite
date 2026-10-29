using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Typrite.Desktop;

public partial class App : Application
{
    /// <summary>Example value from the architecture guide; not a client-approved budget.</summary>
    private const double ReservationDip = 210;

    private readonly List<BarWindow> _bars = [];
    private Mutex? _singleInstance;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Typrite.Desktop", out var createdNew);
        if (!createdNew)
        {
            Exit();
            return;
        }

        // P1: one bar per display at launch. Display add/remove at runtime is P2.
        foreach (var area in DisplayArea.FindAll())
        {
            var bar = new BarWindow(area, ReservationDip);
            bar.Closed += (_, _) =>
            {
                _bars.Remove(bar);
                if (_bars.Count == 0)
                {
                    Exit();
                }
            };
            _bars.Add(bar);
            bar.AppWindow.Show(activateWindow: false);
        }
    }
}
