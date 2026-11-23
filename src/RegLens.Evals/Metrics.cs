using System.Globalization;

namespace RegLens.Evals;

/// <summary>Retrieval metrics over first-hit ranks (spec 0004, P2). A null rank means no expected chunk was found.</summary>
public static class Metrics
{
    /// <summary>Share of questions whose first expected chunk is at rank ≤ k.</summary>
    public static double RecallAt(IReadOnlyList<int?> firstHitRanks, int k)
    {
        ArgumentNullException.ThrowIfNull(firstHitRanks);
        return firstHitRanks.Count == 0 ? 0 : firstHitRanks.Count(r => r <= k) / (double)firstHitRanks.Count;
    }

    /// <summary>Mean of 1/rank of the first expected chunk, counting ranks beyond <paramref name="cutoff"/> as 0.</summary>
    public static double MeanReciprocalRank(IReadOnlyList<int?> firstHitRanks, int cutoff)
    {
        ArgumentNullException.ThrowIfNull(firstHitRanks);
        return firstHitRanks.Count == 0
            ? 0
            : firstHitRanks.Sum(r => r is { } rank && rank <= cutoff ? 1.0 / rank : 0) / firstHitRanks.Count;
    }

    /// <summary>The p-th percentile (0–100) by nearest rank.</summary>
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToList();
        var rank = (int)Math.Ceiling(p / 100 * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    /// <summary>
    /// The similarity floor of D48: the highest floor that refuses none of the answerable questions the floor applies
    /// to (pinned questions bypass it), minus <paramref name="margin"/>, rounded down to two decimals.
    /// </summary>
    public static double Floor(IEnumerable<double> answerableBestSimilarities, double margin)
    {
        ArgumentNullException.ThrowIfNull(answerableBestSimilarities);
        var lowest = answerableBestSimilarities.DefaultIfEmpty(0).Min();
        // Round first: 0.57 − 0.02 is 0.5499999… in binary and must floor to 0.55, not 0.54.
        return Math.Max(0, Math.Floor(Math.Round((lowest - margin) * 100, 6)) / 100);
    }

    public static string Format(double value, int decimals = 3) => value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
}
