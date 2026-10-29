using Typrite.Domain.Partners;

namespace Typrite.Domain.Tests;

public class ReconciliationComparerTests
{
    private static readonly DateOnly Day = new(2026, 10, 30);
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void Identical_counts_match()
    {
        var r = ReconciliationComparer.Compare([new(Day, A, 100)], [new(Day, A, 100)], tolerance: 0.02);

        Assert.Equal(ReconciliationStatus.Matched, r.Status);
        Assert.Empty(r.Mismatches);
        Assert.Equal(100, r.OurTotal);
        Assert.Equal(100, r.PartnerTotal);
    }

    [Fact]
    public void Small_difference_inside_tolerance_still_matches()
    {
        var r = ReconciliationComparer.Compare([new(Day, A, 100)], [new(Day, A, 101)], tolerance: 0.02);

        Assert.Equal(ReconciliationStatus.Matched, r.Status);
    }

    [Fact]
    public void Difference_beyond_tolerance_is_a_mismatch_with_detail()
    {
        var r = ReconciliationComparer.Compare([new(Day, A, 100)], [new(Day, A, 80)], tolerance: 0.02);

        Assert.Equal(ReconciliationStatus.Mismatched, r.Status);
        var m = Assert.Single(r.Mismatches);
        Assert.Equal(A, m.CreativeId);
        Assert.Equal(100, m.Ours);
        Assert.Equal(80, m.Partner);
    }

    [Fact]
    public void Missing_on_either_side_counts_as_zero()
    {
        var r = ReconciliationComparer.Compare([new(Day, A, 10)], [new(Day, B, 5)], tolerance: 0.02);

        Assert.Equal(2, r.Mismatches.Count);
        Assert.Contains(r.Mismatches, m => m.CreativeId == A && m.Partner == 0);
        Assert.Contains(r.Mismatches, m => m.CreativeId == B && m.Ours == 0);
    }

    [Fact]
    public void Both_empty_is_a_match()
    {
        Assert.Equal(ReconciliationStatus.Matched, ReconciliationComparer.Compare([], [], 0.02).Status);
    }
}
