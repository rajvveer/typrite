using System.IO.Pipes;
using Typrite.Agent.Pipe;
using Typrite.Contracts.Agent;

namespace Typrite.Agent.Tests;

public class PipeProtocolTests
{
    [Fact]
    public async Task Request_and_response_round_trip_one_line_each()
    {
        var handler = new PipeRequestHandler(
            token: ct => Task.FromResult(("tok", DateTimeOffset.UnixEpoch.AddDays(1))),
            identity: () => (Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222")),
            nextCommand: () => "restart-bar");

        var ok = await handler.HandleAsync(new PipeRequest(PipeOps.Token), CancellationToken.None);
        var id = await handler.HandleAsync(new PipeRequest(PipeOps.Identity), CancellationToken.None);
        var cmd = await handler.HandleAsync(new PipeRequest(PipeOps.NextCommand), CancellationToken.None);
        var bad = await handler.HandleAsync(new PipeRequest("shutdown"), CancellationToken.None);

        Assert.True(ok.Ok);
        Assert.Equal("tok", ok.Token);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), id.DeviceId);
        Assert.Equal("restart-bar", cmd.Command);
        Assert.False(bad.Ok);
    }

    [Fact]
    public async Task Token_failure_is_reported_not_thrown()
    {
        var handler = new PipeRequestHandler(
            token: ct => throw new HttpRequestException("offline"),
            identity: () => (Guid.NewGuid(), null),
            nextCommand: () => null);

        var res = await handler.HandleAsync(new PipeRequest(PipeOps.Token), CancellationToken.None);

        Assert.False(res.Ok);
        Assert.NotNull(res.Error);
    }

    [Fact]
    public async Task Server_and_client_exchange_json_lines_over_a_real_pipe()
    {
        var pipeName = $"tt{Guid.NewGuid():N}"[..10]; // short: macOS limits socket paths to 104 chars
        var handler = new PipeRequestHandler(ct => Task.FromResult(("tok", DateTimeOffset.UtcNow.AddMinutes(5))), () => (Guid.NewGuid(), null), () => null);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new PipeServer(pipeName, handler);
        var serverTask = server.RunAsync(cts.Token);

        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cts.Token);
        var response = await PipeClient.ExchangeAsync(client, new PipeRequest(PipeOps.Ping), cts.Token);

        Assert.True(response.Ok);
        await cts.CancelAsync();
        await Task.WhenAny(serverTask, Task.Delay(2000));
    }
}
