using Typrite.Domain.Campaigns;

namespace Typrite.Domain.Delivery;

public static class AdSelector
{
    /// <summary>
    /// Picks one creative: active campaigns inside their window, weighted at random, then the first creative whose
    /// format appears earliest in the device's supported list. Null means no fill.
    /// </summary>
    public static (Campaign Campaign, Creative Creative)? Choose(
        IReadOnlyList<(Campaign Campaign, IReadOnlyList<Creative> Creatives)> candidates,
        IReadOnlyList<string> supportedFormats,
        DateTimeOffset now,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(supportedFormats);
        ArgumentNullException.ThrowIfNull(random);

        var eligible = new List<(Campaign Campaign, Creative Creative)>();
        foreach (var (campaign, creatives) in candidates)
        {
            if (campaign.Status != CampaignStatus.Active || now < campaign.StartsAtUtc || now >= campaign.EndsAtUtc || campaign.Weight <= 0)
            {
                continue;
            }

            var best = creatives
                .Select(c => (Creative: c, Rank: IndexOf(supportedFormats, c.Format)))
                .Where(x => x.Rank >= 0)
                .OrderBy(x => x.Rank)
                .Select(x => x.Creative)
                .FirstOrDefault();
            if (best is not null)
            {
                eligible.Add((campaign, best));
            }
        }

        if (eligible.Count == 0)
        {
            return null;
        }

        var total = eligible.Sum(e => e.Campaign.Weight);
        var roll = random.Next(total);
        foreach (var e in eligible)
        {
            roll -= e.Campaign.Weight;
            if (roll < 0)
            {
                return e;
            }
        }

        return eligible[^1];
    }

    private static int IndexOf(IReadOnlyList<string> formats, string format)
    {
        for (var i = 0; i < formats.Count; i++)
        {
            if (string.Equals(formats[i], format, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
