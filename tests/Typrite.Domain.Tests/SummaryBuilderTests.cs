using Typrite.Contracts.Delivery;
using Typrite.Domain.Delivery;

namespace Typrite.Domain.Tests;

public class SummaryBuilderTests
{
    private static readonly DateOnly Day = new(2026, 10, 30);
    private static readonly Guid Campaign = Guid.NewGuid();
    private static readonly Guid Creative = Guid.NewGuid();

    private static RawDeliveryFact Fact(DeliveryEventType type, Guid? device = null, long? duration = null, Guid? creative = null) =>
        new(Campaign, creative ?? Creative, device ?? Guid.NewGuid(), type, duration);

    [Fact]
    public void Counts_events_and_distinct_devices_per_creative_and_type()
    {
        var d1 = Guid.NewGuid();
        var rows = SummaryBuilder.Build(Day,
        [
            Fact(DeliveryEventType.RenderStarted, d1),
            Fact(DeliveryEventType.RenderStarted, d1),
            Fact(DeliveryEventType.RenderStarted),
            Fact(DeliveryEventType.VisibilityObserved, d1),
        ]);

        var render = Assert.Single(rows, r => r.Type == DeliveryEventType.RenderStarted);
        Assert.Equal(3, render.Count);
        Assert.Equal(2, render.DistinctDevices);
        Assert.Equal(Day, render.Day);
        Assert.Equal(Campaign, render.CampaignId);
        Assert.Single(rows, r => r.Type == DeliveryEventType.VisibilityObserved);
    }

    [Fact]
    public void Visible_milliseconds_sum_only_terminal_visibility_events()
    {
        var rows = SummaryBuilder.Build(Day,
        [
            Fact(DeliveryEventType.VisibilityLost, duration: 3000),
            Fact(DeliveryEventType.PlaybackCompleted, duration: 5000),
            Fact(DeliveryEventType.RenderStarted, duration: 999),
        ]);

        Assert.Equal(3000, Assert.Single(rows, r => r.Type == DeliveryEventType.VisibilityLost).VisibleMilliseconds);
        Assert.Equal(5000, Assert.Single(rows, r => r.Type == DeliveryEventType.PlaybackCompleted).VisibleMilliseconds);
        Assert.Equal(0, Assert.Single(rows, r => r.Type == DeliveryEventType.RenderStarted).VisibleMilliseconds);
    }

    [Fact]
    public void Different_creatives_are_separate_rows()
    {
        var other = Guid.NewGuid();
        var rows = SummaryBuilder.Build(Day, [Fact(DeliveryEventType.RenderStarted), Fact(DeliveryEventType.RenderStarted, creative: other)]);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Empty_input_yields_no_rows()
    {
        Assert.Empty(SummaryBuilder.Build(Day, []));
    }
}
