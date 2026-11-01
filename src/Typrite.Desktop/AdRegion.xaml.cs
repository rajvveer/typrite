using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Typrite.Player.Delivery;
using Typrite.Player.Playback;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Typrite.Desktop;

/// <summary>Renders one resolved creative and feeds player state into an <see cref="AdVisibilityTracker"/>.</summary>
public sealed partial class AdRegion : UserControl
{
    private MediaPlayer? _player;
    private AdVisibilityTracker? _tracker;
    private readonly DispatcherQueueTimer _mockTimer;
    private bool _mockEnabled;
    private bool _mockPlaying;
    private bool _surfaceVisible = true;
    private bool _motionStarted;
    private int _mockScene;

    public AdRegion()
    {
        InitializeComponent();
        _mockTimer = DispatcherQueue.CreateTimer();
        _mockTimer.Interval = TimeSpan.FromSeconds(8);
        _mockTimer.Tick += (_, _) =>
        {
            _mockScene = (_mockScene + 1) % 3;
            UpdateMockCopy();
            HeadlineReveal.Begin();
            CampaignProgress.Begin();
        };
        Loaded += (_, _) => PlayMock();
        Unloaded += (_, _) => PauseMock();
    }

    public void EnableMockCampaign(bool enabled)
    {
        _mockEnabled = enabled;
        _mockPlaying = enabled;
        MockCampaign.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateMockCopy();
        if (enabled) PlayMock();
    }

    public void SetPlaybackVisible(bool visible)
    {
        _surfaceVisible = visible;
        if (visible) PlayMock(); else PauseMock();
    }

    private void PlayMock()
    {
        if (!_mockPlaying || !_surfaceVisible || !IsLoaded) return;
        if (_motionStarted)
        {
            CampaignMotion.Resume();
            CampaignProgress.Resume();
        }
        else
        {
            CampaignMotion.Begin();
            CampaignProgress.Begin();
            _motionStarted = true;
        }
        _mockTimer.Start();
    }

    private void PauseMock()
    {
        _mockTimer.Stop();
        if (!_motionStarted) return;
        CampaignMotion.Pause();
        CampaignProgress.Pause();
    }

    private void UpdateMockCopy()
    {
        (CampaignHeadline.Text, CampaignSubtitle.Text) = _mockScene switch
        {
            1 => ("Find your\nfrequency.", "For the way you move."),
            2 => ("Every day.\nElevated.", "Meet your new everyday soundtrack."),
            _ => ("Less noise.\nMore you.", "Wireless sound. Effortless style."),
        };
        CampaignFrame.Text = $"0{_mockScene + 1} / 03";
    }

    private void OnCampaignSizeChanged(object sender, SizeChangedEventArgs e)
    {
        CampaignProduct.Width = e.NewSize.Height * 2.5;
        ProductFade.Margin = new Thickness(0, 0, Math.Max(0, CampaignProduct.Width - ProductFade.Width), 0);
    }

    public async Task ShowAsync(ResolvedCreative creative, AdVisibilityTracker tracker)
    {
        Stop();
        _tracker = tracker;

        var mock = _mockEnabled && creative.Source == CreativeSource.House;
        MockCampaign.Visibility = mock ? Visibility.Visible : Visibility.Collapsed;
        SponsorBadge.Visibility = mock ? Visibility.Collapsed : Visibility.Visible;
        if (mock)
        {
            Still.Visibility = Visibility.Collapsed;
            Video.Visibility = Visibility.Collapsed;
            _mockPlaying = true;
            PlayMock();
            _tracker.RenderStarted();
            return;
        }

        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(creative.Bytes.AsBuffer());
        stream.Seek(0);

        if (creative.Manifest.MediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            _player = new MediaPlayer { IsMuted = true, Volume = 0, AutoPlay = true, IsLoopingEnabled = false };
            _player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() => _tracker?.RenderStarted());
            _player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() => _tracker?.PlaybackCompleted());
            _player.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(() => _tracker?.RenderFailed(e.ErrorMessage));
            _player.PlaybackSession.PositionChanged += (s, _) =>
            {
                var ms = (int)s.Position.TotalMilliseconds;
                DispatcherQueue.TryEnqueue(() => _tracker?.PlaybackProgress(ms));
            };
            _player.Source = MediaSource.CreateFromStream(stream, creative.Manifest.MediaType);
            Video.SetMediaPlayer(_player);
            Still.Visibility = Visibility.Collapsed;
            Video.Visibility = Visibility.Visible;
            return;
        }

        var bitmap = new BitmapImage();
        Still.ImageOpened += OnStillOpened;
        Still.ImageFailed += OnStillFailed;
        Still.Source = bitmap;
        await bitmap.SetSourceAsync(stream);
        Video.Visibility = Visibility.Collapsed;
        Still.Visibility = Visibility.Visible;
    }

    /// <summary>Ends the current playback (next creative, bar closing). Stills are "completed" when replaced.</summary>
    public void Stop()
    {
        _mockPlaying = false;
        _mockTimer.Stop();
        CampaignMotion.Stop();
        CampaignProgress.Stop();
        HeadlineReveal.Stop();
        _motionStarted = false;
        Still.ImageOpened -= OnStillOpened;
        Still.ImageFailed -= OnStillFailed;
        _tracker?.PlaybackCompleted();
        _tracker = null;

        if (_player is not null)
        {
            Video.SetMediaPlayer(null);
            _player.Dispose();
            _player = null;
        }
    }

    private void OnStillOpened(object sender, RoutedEventArgs e) => _tracker?.RenderStarted();

    private void OnStillFailed(object sender, ExceptionRoutedEventArgs e) => _tracker?.RenderFailed(e.ErrorMessage);
}
