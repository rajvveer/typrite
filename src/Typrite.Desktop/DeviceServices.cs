using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.UI.Dispatching;
using Typrite.Contracts;
using Typrite.Contracts.Configuration;
using Typrite.Contracts.Delivery;
using Typrite.Contracts.Weather;
using Typrite.LocalStore;
using Typrite.Player.Creatives;
using Typrite.Player.Delivery;

namespace Typrite.Desktop;

/// <summary>Per-process services shared by every bar window: identity, outbox, cache, delivery pipeline, uploader.</summary>
public sealed class DeviceServices : IDisposable
{
    private static readonly TimeSpan UploadInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UploadInitialBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UploadMaxBackoff = TimeSpan.FromMinutes(5);

    private readonly AgentPipeClient _agent = new();
    private readonly HttpClient _http;
    private readonly EventOutbox _outbox;
    private readonly CreativeCache _cache;
    private readonly EventUploader _uploader;
    private readonly DispatcherQueueTimer _uploadTimer;
    private int _uploadFailures;

    public DeviceServices(DesktopSettings settings, DispatcherQueue dispatcher)
    {
        var dir = DesktopSettings.DataDirectory;
        SessionId = Guid.NewGuid().ToString("N");
        Configuration = DeviceConfiguration.Default;

        // Prefer the agent's enrolled identity and brokered tokens; fall back to the development credential (guide section 3).
        var agentIdentity = _agent.TryIdentityAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (agentIdentity is not null)
        {
            UsesAgent = true;
            DeviceId = agentIdentity.DeviceId;
        }
        else
        {
            Credential = DevDeviceCredential.LoadOrCreate(dir);
            DeviceId = Credential.DeviceId;
        }

        _http = new HttpClient { BaseAddress = settings.ApiBaseUrl, Timeout = TimeSpan.FromSeconds(20) };
        Api = new HttpDeliveryApi(_http, DeviceId, TokenAsync);

        _outbox = new EventOutbox(Path.Combine(dir, "events.db"), TimeProvider.System, maxCount: 50_000, maxAge: TimeSpan.FromDays(7));
        _cache = new CreativeCache(new FileCreativeStore(Path.Combine(dir, "creatives")), Configuration.Ads.CacheMaxMb * 1024L * 1024L);
        _uploader = new EventUploader(_outbox, Api, batchSize: 200);

        var houseBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "house-400x225.png"));
        var house = new HouseCreative(
            new CreativeManifest(Guid.Empty, "400x225", "image/png", houseBytes.Length,
                Convert.ToHexStringLower(SHA256.HashData(houseBytes)), null, new Uri("https://local/house-400x225.png"), null),
            houseBytes);
        Pipeline = new AdDeliveryPipeline(Api, _cache, CreativePolicy.Default, house,
            new DeviceContext(DeviceId, SessionId, Configuration.ConfigurationVersion), TimeProvider.System, Record);

        _uploadTimer = dispatcher.CreateTimer();
        _uploadTimer.Interval = UploadInterval;
        _uploadTimer.Tick += async (_, _) => await UploadAsync();
        _uploadTimer.Start();
    }

    /// <summary>Development credential; null when the agent provides identity and tokens.</summary>
    public DevDeviceCredential? Credential { get; }

    public Guid DeviceId { get; }

    public bool UsesAgent { get; }

    public string SessionId { get; }

    public DeviceConfiguration Configuration { get; private set; }

    public IDeliveryApi Api { get; }

    public AdDeliveryPipeline Pipeline { get; }

    /// <summary>Every technical event from every bar lands here and is uploaded later; nothing is lost on network failure.</summary>
    public void Record(DeliveryEvent e) => _outbox.Enqueue(e);

    public async Task RegisterDevAsync(CancellationToken ct)
    {
        if (Credential is null)
        {
            return;
        }

        using var res = await _http.PostAsJsonAsync("api/v1/devices/dev-register",
            new { deviceId = Credential.DeviceId, secret = Credential.Secret, appVersion = typeof(App).Assembly.GetName().Version?.ToString() },
            ContractsJson.Options, ct);
        res.EnsureSuccessStatusCode();
    }

    /// <summary>Next command the agent is holding for the desktop, if any.</summary>
    public Task<string?> TryNextAgentCommandAsync(CancellationToken ct) => _agent.TryNextCommandAsync(ct);

    private async ValueTask<string> TokenAsync(CancellationToken ct)
    {
        if (UsesAgent)
        {
            return await _agent.TryTokenAsync(ct) ?? throw new HttpRequestException("agent did not provide a device token");
        }

        return Credential!.Secret;
    }

    public async Task RefreshConfigurationAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"api/v1/devices/{DeviceId}/configuration");
        req.Headers.Authorization = new("Bearer", await TokenAsync(ct));
        using var res = await _http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode)
        {
            Configuration = (await res.Content.ReadFromJsonAsync<DeviceConfiguration>(ContractsJson.Options, ct))!;
        }
    }

    public async Task<WeatherReport?> GetWeatherAsync(double? lat, double? lon, CancellationToken ct)
    {
        var query = lat is { } la && lon is { } lo
            ? $"api/v1/weather?lat={Math.Round(la, 2)}&lon={Math.Round(lo, 2)}"
            : "api/v1/weather";
        using var req = new HttpRequestMessage(HttpMethod.Get, query);
        req.Headers.Authorization = new("Bearer", await TokenAsync(ct));
        using var res = await _http.SendAsync(req, ct);
        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<WeatherReport>(ContractsJson.Options, ct) : null;
    }

    public void Dispose()
    {
        _uploadTimer.Stop();
        _outbox.PruneExpired();
        _outbox.Dispose();
        _cache.Dispose();
        _http.Dispose();
    }

    private async Task UploadAsync()
    {
        var result = await _uploader.DrainOnceAsync(CancellationToken.None);
        if (result.Outcome == UploadOutcome.Failed)
        {
            _uploadFailures++;
            _uploadTimer.Interval = RetryBackoff.Delay(_uploadFailures, UploadInitialBackoff, UploadMaxBackoff, jitter: 0.2);
            return;
        }

        _uploadFailures = 0;
        _uploadTimer.Interval = UploadInterval;
        if (result.Outcome == UploadOutcome.Sent)
        {
            await _uploader.DrainAllAsync(CancellationToken.None);
        }
    }
}
