using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Typrite.Contracts;
using Typrite.Contracts.Agent;

namespace Typrite.Agent.Pipe;

/// <summary>
/// Local named-pipe server. On Windows the ACL lets interactive users read and write and keeps full control with the
/// service account; the pipe is never reachable over the network.
/// </summary>
public sealed class PipeServer(string pipeName, PipeRequestHandler handler)
{
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = Create();
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _ = ServeAsync(server, ct);
        }
    }

    private NamedPipeServerStream Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindows();
        }

        return new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    [SupportedOSPlatform("windows")]
    private NamedPipeServerStream CreateWindows()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, inBufferSize: 0, outBufferSize: 0, security);
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using (pipe)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await PipeClient.ReadLineAsync(pipe, ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        return;
                    }

                    PipeResponse response;
                    try
                    {
                        var request = JsonSerializer.Deserialize<PipeRequest>(line, ContractsJson.Options);
                        response = request is null
                            ? new PipeResponse(Ok: false, Error: "empty request")
                            : await handler.HandleAsync(request, ct).ConfigureAwait(false);
                    }
                    catch (JsonException ex)
                    {
                        response = new PipeResponse(Ok: false, Error: "malformed request: " + ex.Message);
                    }

                    await PipeClient.WriteLineAsync(pipe, response, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException)
            {
                // Client went away or shutdown; the loop accepts the next connection.
            }
        }
    }
}
