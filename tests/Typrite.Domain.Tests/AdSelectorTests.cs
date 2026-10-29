using Typrite.Domain.Campaigns;
using Typrite.Domain.Delivery;

namespace Typrite.Domain.Tests;

public class AdSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static Campaign Campaign(CampaignStatus status = CampaignStatus.Active, DateTimeOffset? starts = null, DateTimeOffset? ends = null, int weight = 1) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Name = "c",
            Status = status,
            StartsAtUtc = starts ?? Now.AddDays(-1),
            EndsAtUtc = ends ?? Now.AddDays(1),
            Weight = weight,
        };

    private static Creative Creative(Campaign c, string format = "400x225") =>
        new() { Id = Guid.NewGuid(), CampaignId = c.Id, Format = format, MediaType = "image/png", SizeBytes = 10, Sha256 = new string('a', 64), StorageKey = "k" };

    [Fact]
    public void Picks_creative_from_active_campaign_in_supported_format()
    {
        var c = Campaign();
        var cr = Creative(c);

        var pick = AdSelector.Choose([(c, [cr])], ["400x225"], Now, new Random(1));

        Assert.Equal(cr.Id, pick!.Value.Creative.Id);
    }

    [Theory]
    [InlineData(CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Approved)]
    [InlineData(CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Ended)]
    public void Only_active_campaigns_are_eligible(CampaignStatus status)
    {
        var c = Campaign(status);

        Assert.Null(AdSelector.Choose([(c, [Creative(c)])], ["400x225"], Now, new Random(1)));
    }

    [Fact]
    public void Campaign_outside_its_window_is_not_eligible()
    {
        var future = Campaign(starts: Now.AddHours(1));
        var past = Campaign(ends: Now.AddHours(-1));

        Assert.Null(AdSelector.Choose([(future, [Creative(future)]), (past, [Creative(past)])], ["400x225"], Now, new Random(1)));
    }

    [Fact]
    public void Unsupported_formats_are_skipped_and_the_first_supported_preferred()
    {
        var c = Campaign();
        var wide = Creative(c, "728x90");
        var alt = Creative(c, "384x216");
        var primary = Creative(c, "400x225");

        var pick = AdSelector.Choose([(c, [wide, alt, primary])], ["400x225", "384x216"], Now, new Random(1));

        Assert.Equal(primary.Id, pick!.Value.Creative.Id);
    }

    [Fact]
    public void Selection_is_weighted_across_campaigns()
    {
        var heavy = Campaign(weight: 9);
        var light = Campaign(weight: 1);
        var rng = new Random(42);
        var heavyPicks = 0;

        for (var i = 0; i < 1000; i++)
        {
            var pick = AdSelector.Choose([(heavy, [Creative(heavy)]), (light, [Creative(light)])], ["400x225"], Now, rng);
            if (pick!.Value.Campaign.Id == heavy.Id)
            {
                heavyPicks++;
            }
        }

        Assert.InRange(heavyPicks, 850, 950);
    }

    [Fact]
    public void No_campaigns_means_no_fill()
    {
        Assert.Null(AdSelector.Choose([], ["400x225"], Now, new Random(1)));
    }
}
