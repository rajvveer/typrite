using Typrite.Contracts.Delivery;
using Typrite.Domain.Delivery;

namespace Typrite.Domain.Tests;

public class EventIngestRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Device = Guid.NewGuid();

    private static DeliveryEvent Event(Guid? device = null, DateTimeOffset? time = null, int schema = 1, string display = "d") =>
        new(Guid.NewGuid(), device ?? Device, "s", display, null, null, DeliveryEventType.RenderStarted, time ?? Now, null, 1, schema);

    [Fact]
    public void Well_formed_event_from_the_authenticated_device_is_accepted()
    {
        Assert.Null(EventIngestRules.Reject(Event(), Device, Now));
    }

    [Fact]
    public void Event_claiming_another_device_is_rejected()
    {
        Assert.NotNull(EventIngestRules.Reject(Event(device: Guid.NewGuid()), Device, Now));
    }

    [Fact]
    public void Unknown_schema_version_is_rejected()
    {
        Assert.NotNull(EventIngestRules.Reject(Event(schema: 2), Device, Now));
    }

    [Fact]
    public void Device_time_far_in_the_future_is_rejected()
    {
        Assert.NotNull(EventIngestRules.Reject(Event(time: Now.AddHours(2)), Device, Now));
        Assert.Null(EventIngestRules.Reject(Event(time: Now.AddMinutes(5)), Device, Now));
    }

    [Fact]
    public void Device_time_older_than_retention_is_rejected()
    {
        Assert.NotNull(EventIngestRules.Reject(Event(time: Now.AddDays(-31)), Device, Now));
        Assert.Null(EventIngestRules.Reject(Event(time: Now.AddDays(-6)), Device, Now));
    }

    [Fact]
    public void Blank_display_key_is_rejected()
    {
        Assert.NotNull(EventIngestRules.Reject(Event(display: " "), Device, Now));
    }

    [Fact]
    public void Empty_client_event_id_is_rejected()
    {
        Assert.NotNull(EventIngestRules.Reject(Event() with { ClientEventId = Guid.Empty }, Device, Now));
    }
}
