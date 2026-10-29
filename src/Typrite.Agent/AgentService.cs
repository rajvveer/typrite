using System.IO.Compression;
using System.Runtime.InteropServices;
using Typrite.Agent.Commands;
using Typrite.Agent.Identity;
using Typrite.Agent.Pipe;
using Typrite.Agent.Updates;
using Typrite.Contracts.Agent;
using Typrite.Domain.Devices;

namespace Typrite.Agent;

/// <summary>
/// The agent's main loop: enroll once, then heartbeat, poll commands, check releases, and serve the desktop pipe.
/// Every network failure is logged and retried on the next tick; the desktop never depends on this loop to render.
/// </summary>
public sealed partial class AgentService(AgentOptions options, DeviceApiClient api, TimeProvider time, ILogger<AgentService> log, ILoggerFactory loggers) : BackgroundService
{
    private static readonly string AppVersion = typeof(AgentService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly string Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    private readonly DesktopCommandBuffer _desktopCommands = new();
    private AgentState _state = AgentState.Empty;
    private DeviceKeyStore? _key;
    private TokenCache? _tokens;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dir = options.ResolvedDataDirectory;
        _state = AgentState.Load(dir);
        _key = DeviceKeyStore.Open(dir);
        _tokens = new TokenCache(time, TimeSpan.FromMinutes(5), ct => api.TokenAsync(_state.DeviceId!.Value, _key.Key, ct));

        var pipe = new PipeServer(AgentPipe.Name, new PipeRequestHandler(
            token: async ct =>
            {
                if (!_state.IsEnrolled)
                {
                    throw new InvalidOperationException("device is not enrolled");
                }

                var t = await _tokens.GetAsync(ct).ConfigureAwait(false);
                return (t, time.GetUtcNow().AddMinutes(25));
            },
            identity: () => (_state.DeviceId ?? Guid.Empty, _state.AccountId),
            nextCommand: _desktopCommands.TryDequeue));
        _ = pipe.RunAsync(stoppingToken);

        var nextHeartbeat = time.GetUtcNow();
        var nextCommands = time.GetUtcNow();
        var nextRelease = time.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_state.IsEnrolled)
                {
                    await TryEnrollAsync(dir, stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    var now = time.GetUtcNow();
                    if (now >= nextHeartbeat)
                    {
                        await HeartbeatAsync(stoppingToken).ConfigureAwait(false);
                        nextHeartbeat = now.AddSeconds(options.HeartbeatSeconds);
                    }

                    if (now >= nextCommands)
                    {
                        await PollCommandsAsync(dir, stoppingToken).ConfigureAwait(false);
                        nextCommands = now.AddSeconds(options.CommandPollSeconds);
                    }

                    if (now >= nextRelease)
                    {
                        await CheckReleaseAsync(dir, stoppingToken).ConfigureAwait(false);
                        nextRelease = now.AddMinutes(options.ReleaseCheckMinutes);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                LogTransient(log, ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(15), time, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task TryEnrollAsync(string dir, CancellationToken ct)
    {
        var claimPath = Path.Combine(dir, "claim.token");
        var claim = options.ClaimToken ?? (File.Exists(claimPath) ? File.ReadAllText(claimPath).Trim() : null);
        if (string.IsNullOrWhiteSpace(claim))
        {
            LogNoClaim(log);
            return;
        }

        var result = await api.EnrollAsync(claim, _key!.PublicKeySpkiBase64, AppVersion, Architecture, ct).ConfigureAwait(false);
        _state = _state with { DeviceId = result.DeviceId, AccountId = result.AccountId };
        _state.Save(dir);
        if (File.Exists(claimPath))
        {
            File.Delete(claimPath); // single use; never leave it on disk
        }

        LogEnrolled(log, result.DeviceId);
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        var token = await _tokens!.GetAsync(ct).ConfigureAwait(false);
        var health = new
        {
            uptimeSeconds = Environment.TickCount64 / 1000,
            workingSetMb = Environment.WorkingSet / (1024 * 1024),
            os = RuntimeInformation.OSDescription,
        };
        await api.HeartbeatAsync(_state.DeviceId!.Value, token, health, AppVersion, Architecture, ct).ConfigureAwait(false);
    }

    private async Task PollCommandsAsync(string dir, CancellationToken ct)
    {
        var token = await _tokens!.GetAsync(ct).ConfigureAwait(false);
        var deviceId = _state.DeviceId!.Value;
        foreach (var command in await api.CommandsAsync(deviceId, token, ct).ConfigureAwait(false))
        {
            object result;
            switch (CommandRouter.TargetOf(command.Type))
            {
                case CommandTarget.Desktop:
                    _desktopCommands.Enqueue(command.Type);
                    result = new { forwarded = "desktop" };
                    break;
                case CommandTarget.Both:
                    _desktopCommands.Enqueue(command.Type);
                    result = new { forwarded = "desktop", agent = "ok" };
                    break;
                case CommandTarget.Agent when command.Type == RemoteCommandTypes.UploadDiagnostics:
                    result = await UploadDiagnosticsAsync(dir, deviceId, token, ct).ConfigureAwait(false);
                    break;
                default:
                    result = new { error = "command type not supported by this agent version" };
                    break;
            }

            await api.AckAsync(deviceId, command.Id, token, result, ct).ConfigureAwait(false);
            LogCommand(log, command.Type, command.Id);
        }
    }

    /// <summary>Zips the agent's own logs. Never screen content, never user files.</summary>
    private async Task<object> UploadDiagnosticsAsync(string dir, Guid deviceId, string token, CancellationToken ct)
    {
        var logDir = Path.Combine(dir, "logs");
        if (!Directory.Exists(logDir))
        {
            return new { uploaded = false, reason = "no logs" };
        }

        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(logDir, "*.log"))
            {
                zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
            }
        }

        await api.UploadDiagnosticsAsync(deviceId, token, zipStream.ToArray(), ct).ConfigureAwait(false);
        return new { uploaded = true, bytes = zipStream.Length };
    }

    private async Task CheckReleaseAsync(string dir, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.PublisherPublicKeyPem))
        {
            return;
        }

        var token = await _tokens!.GetAsync(ct).ConfigureAwait(false);
        var current = await api.CurrentReleaseAsync(_state.DeviceId!.Value, token, ct).ConfigureAwait(false);
        if (current is null)
        {
            return;
        }

        var decision = UpdateDecider.Decide(current, AppVersion, Architecture, options.PublisherPublicKeyPem);
        LogReleaseDecision(log, current.Manifest.Version, decision.Action, decision.Reason);
        if (decision.Action != UpdateAction.Install || _state.LastAttemptedVersion == current.Manifest.Version)
        {
            return;
        }

        _state = _state with { LastAttemptedVersion = current.Manifest.Version };
        _state.Save(dir);
        var installer = new UpdateInstaller(api, Path.Combine(dir, "staging"), loggers.CreateLogger<UpdateInstaller>());
        if (await installer.InstallAsync(current.Manifest, ct).ConfigureAwait(false))
        {
            _state = _state with { LastInstalledVersion = current.Manifest.Version };
            _state.Save(dir);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transient failure: {Message}")]
    private static partial void LogTransient(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Not enrolled and no claim token present; waiting")]
    private static partial void LogNoClaim(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enrolled as device {DeviceId}")]
    private static partial void LogEnrolled(ILogger logger, Guid deviceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled command {Type} ({Id})")]
    private static partial void LogCommand(ILogger logger, string type, Guid id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Release {Version}: {Action} ({Reason})")]
    private static partial void LogReleaseDecision(ILogger logger, string version, UpdateAction action, string reason);
}
