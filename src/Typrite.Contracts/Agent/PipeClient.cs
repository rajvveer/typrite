using System.Text;
using System.Text.Json;

namespace Typrite.Contracts.Agent;

/// <summary>One request, one response, newline-delimited JSON. Shared by the agent's server and the desktop's client.</summary>
public static class PipeClient
{
    public const int MaxLineBytes = 16 * 1024;

    public static async Task<PipeResponse> ExchangeAsync(Stream pipe, PipeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        await WriteLineAsync(pipe, request, ct).ConfigureAwait(false);
        var line = await ReadLineAsync(pipe, ct).ConfigureAwait(false)
            ?? throw new IOException("Agent closed the pipe without answering.");
        return JsonSerializer.Deserialize<PipeResponse>(line, ContractsJson.Options)
            ?? throw new IOException("Agent answered with an empty response.");
    }

    public static async Task WriteLineAsync<T>(Stream pipe, T message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, ContractsJson.Options) + "\n");
        await pipe.WriteAsync(bytes, ct).ConfigureAwait(false);
        await pipe.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads up to a newline; null at end of stream. Bounded so a misbehaving peer cannot exhaust memory.</summary>
    public static async Task<string?> ReadLineAsync(Stream pipe, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = await pipe.ReadAsync(one, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (one[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (buffer.Length >= MaxLineBytes)
            {
                throw new IOException("Pipe message exceeded the size limit.");
            }

            buffer.WriteByte(one[0]);
        }
    }
}
