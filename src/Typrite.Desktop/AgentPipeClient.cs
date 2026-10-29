using System.IO.Pipes;
using Typrite.Contracts.Agent;

namespace Typrite.Desktop;

/// <summary>Talks to the local agent. Every call is best-effort: no agent means null, never an exception to the caller.</summary>
public sealed class AgentPipeClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);

    public sealed record AgentIdentity(Guid DeviceId, Guid? AccountId);

    public async Task<AgentIdentity?> TryIdentityAsync(CancellationToken ct)
    {
        var r = await TryExchangeAsync(new PipeRequest(PipeOps.Identity), ct);
        return r is { Ok: true, DeviceId: { } id } && id != Guid.Empty ? new AgentIdentity(id, r.AccountId) : null;
    }

    public async Task<string?> TryTokenAsync(CancellationToken ct)
    {
        var r = await TryExchangeAsync(new PipeRequest(PipeOps.Token), ct);
        return r is { Ok: true, Token: { } token } ? token : null;
    }

    public async Task<string?> TryNextCommandAsync(CancellationToken ct)
    {
        var r = await TryExchangeAsync(new PipeRequest(PipeOps.NextCommand), ct);
        return r is { Ok: true } ? r.Command : null;
    }

    private static async Task<PipeResponse?> TryExchangeAsync(PipeRequest request, CancellationToken ct)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", AgentPipe.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            await pipe.ConnectAsync(timeout.Token);
            return await PipeClient.ExchangeAsync(pipe, request, ct);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
