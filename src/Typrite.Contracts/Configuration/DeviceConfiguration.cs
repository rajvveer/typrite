namespace Typrite.Contracts.Configuration;

public sealed record LayoutSettings(string Position, double ReservationDip, string CreativeFit);

public sealed record OverlayApp(string ExecutablePath, string SignerSubject);

public sealed record FullscreenSettings(string DefaultAction, IReadOnlyList<OverlayApp> AllowlistedOverlayApps);

public sealed record AdSettings(bool Muted, IReadOnlyList<string> Formats, int CacheMaxMb, int DecisionIntervalSeconds);

public sealed record WeatherSettings(string Source, int RefreshSeconds);

public sealed record HealthSettings(int HeartbeatSeconds);

/// <summary>Versioned device policy. Values are examples until the client approves budgets.</summary>
public sealed record DeviceConfiguration(
    int SchemaVersion,
    int ConfigurationVersion,
    DateTimeOffset ExpiresAtUtc,
    LayoutSettings Layout,
    FullscreenSettings Fullscreen,
    AdSettings Ads,
    WeatherSettings Weather,
    HealthSettings Health)
{
    public static DeviceConfiguration Default { get; } = new(
        SchemaVersion: 1,
        ConfigurationVersion: 0,
        ExpiresAtUtc: DateTimeOffset.MaxValue,
        Layout: new LayoutSettings("bottom", 210, "contain"),
        Fullscreen: new FullscreenSettings("yield", []),
        Ads: new AdSettings(Muted: true, ["400x225", "384x216", "300x250"], CacheMaxMb: 300, DecisionIntervalSeconds: 30),
        Weather: new WeatherSettings("windowsLocationThenIp", 900),
        Health: new HealthSettings(300));
}
