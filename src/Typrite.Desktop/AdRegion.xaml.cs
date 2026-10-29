using System.Runtime.InteropServices.WindowsRuntime;
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

    public AdRegion() => InitializeComponent();

    public async Task ShowAsync(ResolvedCreative creative, AdVisibilityTracker tracker)
    {
        Stop();
        _tracker = tracker;

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
