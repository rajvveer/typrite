namespace Typrite.Domain.Partners;

public readonly record struct DailyCreativeCount(DateOnly Day, Guid CreativeId, long Count);

public sealed record ReconciliationMismatch(DateOnly Day, Guid CreativeId, long Ours, long Partner);

public sealed record ReconciliationResult(ReconciliationStatus Status, long OurTotal, long PartnerTotal, IReadOnlyList<ReconciliationMismatch> Mismatches);

public static class ReconciliationComparer
{
    /// <summary>Compares per-day, per-creative counts. A pair is a mismatch when the relative gap exceeds <paramref name="tolerance"/>.</summary>
    public static ReconciliationResult Compare(IReadOnlyList<DailyCreativeCount> ours, IReadOnlyList<DailyCreativeCount> partner, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(ours);
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        var o = ours.ToDictionary(x => (x.Day, x.CreativeId), x => x.Count);
        var p = partner.ToDictionary(x => (x.Day, x.CreativeId), x => x.Count);
        var mismatches = new List<ReconciliationMismatch>();

        foreach (var key in o.Keys.Union(p.Keys).OrderBy(k => k.Day).ThenBy(k => k.CreativeId))
        {
            var a = o.GetValueOrDefault(key);
            var b = p.GetValueOrDefault(key);
            var largest = Math.Max(a, b);
            if (largest > 0 && Math.Abs(a - b) / (double)largest > tolerance)
            {
                mismatches.Add(new ReconciliationMismatch(key.Day, key.CreativeId, a, b));
            }
        }

        return new ReconciliationResult(
            mismatches.Count == 0 ? ReconciliationStatus.Matched : ReconciliationStatus.Mismatched,
            o.Values.Sum(),
            p.Values.Sum(),
            mismatches);
    }
}
