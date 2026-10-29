using Typrite.Agent.Commands;

namespace Typrite.Agent.Tests;

public class CommandRouterTests
{
    [Theory]
    [InlineData("enter-safe-mode", CommandTarget.Desktop)]
    [InlineData("leave-safe-mode", CommandTarget.Desktop)]
    [InlineData("restart-bar", CommandTarget.Desktop)]
    [InlineData("refresh-configuration", CommandTarget.Both)]
    [InlineData("upload-diagnostics", CommandTarget.Agent)]
    public void Known_commands_route_to_the_right_component(string type, CommandTarget expected)
    {
        Assert.Equal(expected, CommandRouter.TargetOf(type));
    }

    [Fact]
    public void Unknown_commands_are_rejected_rather_than_forwarded()
    {
        Assert.Equal(CommandTarget.Reject, CommandRouter.TargetOf("format-disk"));
        Assert.Equal(CommandTarget.Reject, CommandRouter.TargetOf(""));
    }

    [Fact]
    public void Desktop_queue_hands_out_each_command_once_in_order()
    {
        var q = new DesktopCommandBuffer();
        q.Enqueue("enter-safe-mode");
        q.Enqueue("restart-bar");

        Assert.Equal("enter-safe-mode", q.TryDequeue());
        Assert.Equal("restart-bar", q.TryDequeue());
        Assert.Null(q.TryDequeue());
    }

    [Fact]
    public void Desktop_queue_is_bounded_and_keeps_the_newest()
    {
        var q = new DesktopCommandBuffer(capacity: 2);
        q.Enqueue("a");
        q.Enqueue("b");
        q.Enqueue("c");

        Assert.Equal("b", q.TryDequeue());
        Assert.Equal("c", q.TryDequeue());
    }
}
