using Typrite.Domain.Releases;

namespace Typrite.Domain.Tests;

public class RolloutCohortTests
{
    [Fact]
    public void Zero_percent_includes_nobody_and_hundred_includes_everybody()
    {
        var id = Guid.NewGuid();

        Assert.False(RolloutCohort.IsIncluded(id, 0));
        Assert.True(RolloutCohort.IsIncluded(id, 100));
    }

    [Fact]
    public void Membership_is_stable_for_a_device()
    {
        var id = Guid.NewGuid();

        Assert.Equal(RolloutCohort.IsIncluded(id, 37), RolloutCohort.IsIncluded(id, 37));
    }

    [Fact]
    public void Raising_the_percentage_never_removes_a_device()
    {
        var ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList();

        foreach (var id in ids)
        {
            if (RolloutCohort.IsIncluded(id, 20))
            {
                Assert.True(RolloutCohort.IsIncluded(id, 50));
            }
        }
    }

    [Fact]
    public void Roughly_the_requested_share_is_included()
    {
        var included = Enumerable.Range(0, 5000).Count(_ => RolloutCohort.IsIncluded(Guid.NewGuid(), 25));

        Assert.InRange(included, 1100, 1400);
    }

    [Fact]
    public void Percent_out_of_range_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RolloutCohort.IsIncluded(Guid.NewGuid(), 101));
    }
}
