using Typrite.Domain.Devices;

namespace Typrite.Agent.Commands;

public enum CommandTarget
{
    Reject,
    Agent,
    Desktop,
    Both,
}

public static class CommandRouter
{
    public static CommandTarget TargetOf(string type) => type switch
    {
        RemoteCommandTypes.EnterSafeMode or RemoteCommandTypes.LeaveSafeMode or RemoteCommandTypes.RestartBar => CommandTarget.Desktop,
        RemoteCommandTypes.RefreshConfiguration => CommandTarget.Both,
        RemoteCommandTypes.UploadDiagnostics => CommandTarget.Agent,
        _ => CommandTarget.Reject,
    };
}

/// <summary>Commands waiting for the desktop to pick up over the pipe. Bounded; the newest win because they supersede older state.</summary>
public sealed class DesktopCommandBuffer(int capacity = 16)
{
    private readonly Queue<string> _queue = new();
    private readonly Lock _gate = new();

    public void Enqueue(string command)
    {
        lock (_gate)
        {
            while (_queue.Count >= capacity)
            {
                _queue.Dequeue();
            }

            _queue.Enqueue(command);
        }
    }

    public string? TryDequeue()
    {
        lock (_gate)
        {
            return _queue.TryDequeue(out var c) ? c : null;
        }
    }
}
