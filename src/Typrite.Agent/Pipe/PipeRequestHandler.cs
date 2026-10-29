using Typrite.Contracts.Agent;

namespace Typrite.Agent.Pipe;

/// <summary>Answers desktop requests. The desktop can ask for a token, its identity, or the next command; nothing else.</summary>
public sealed class PipeRequestHandler(
    Func<CancellationToken, Task<(string Token, DateTimeOffset ExpiresAtUtc)>> token,
    Func<(Guid DeviceId, Guid? AccountId)> identity,
    Func<string?> nextCommand)
{
    public async Task<PipeResponse> HandleAsync(PipeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request.Op)
        {
            case PipeOps.Ping:
                return new PipeResponse(Ok: true);

            case PipeOps.Identity:
            {
                var (deviceId, accountId) = identity();
                return new PipeResponse(Ok: true, DeviceId: deviceId, AccountId: accountId);
            }

            case PipeOps.NextCommand:
                return new PipeResponse(Ok: true, Command: nextCommand());

            case PipeOps.Token:
                try
                {
                    var (value, expires) = await token(ct).ConfigureAwait(false);
                    var (deviceId, accountId) = identity();
                    return new PipeResponse(Ok: true, Token: value, ExpiresAtUtc: expires, DeviceId: deviceId, AccountId: accountId);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
                {
                    return new PipeResponse(Ok: false, Error: ex.Message);
                }

            default:
                return new PipeResponse(Ok: false, Error: $"unknown op '{request.Op}'");
        }
    }
}
