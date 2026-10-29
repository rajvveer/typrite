using Typrite.Domain.Campaigns;

namespace Typrite.Domain.Tests;

public class CampaignLifecycleTests
{
    [Theory]
    [InlineData(CampaignStatus.Draft, CampaignStatus.Approved)]
    [InlineData(CampaignStatus.Approved, CampaignStatus.Active)]
    [InlineData(CampaignStatus.Active, CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Paused, CampaignStatus.Active)]
    [InlineData(CampaignStatus.Active, CampaignStatus.Ended)]
    [InlineData(CampaignStatus.Paused, CampaignStatus.Ended)]
    [InlineData(CampaignStatus.Approved, CampaignStatus.Ended)]
    public void Allowed_transitions(CampaignStatus from, CampaignStatus to)
    {
        Assert.True(CampaignLifecycle.CanTransition(from, to));
    }

    [Theory]
    [InlineData(CampaignStatus.Draft, CampaignStatus.Active)]
    [InlineData(CampaignStatus.Draft, CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Ended, CampaignStatus.Active)]
    [InlineData(CampaignStatus.Ended, CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Active, CampaignStatus.Draft)]
    [InlineData(CampaignStatus.Active, CampaignStatus.Active)]
    public void Forbidden_transitions(CampaignStatus from, CampaignStatus to)
    {
        Assert.False(CampaignLifecycle.CanTransition(from, to));
    }

    [Fact]
    public void Approval_requires_at_least_one_creative()
    {
        Assert.False(CampaignLifecycle.CanApprove(creativeCount: 0));
        Assert.True(CampaignLifecycle.CanApprove(creativeCount: 1));
    }
}
