namespace StreamNetMon.Core;

public static class Downsample
{
    /// <summary>
    /// Reduces a long series to at most <paramref name="bins"/> points for drawing, keeping the
    /// worst (highest) value in each stretch so short spikes stay visible. A stretch with no
    /// values becomes a gap.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset At, double? Value)> Max(
        IReadOnlyList<(DateTimeOffset At, double? Value)> points, DateTimeOffset from, DateTimeOffset to, int bins)
    {
        if (points.Count <= bins || bins <= 0 || to <= from) return points;

        var width = (to - from).Ticks / (double)bins;
        var best = new double?[bins];
        foreach (var (at, value) in points)
        {
            if (value is not { } v || at < from || at > to) continue;
            var bin = Math.Min(bins - 1, (int)((at - from).Ticks / width));
            if (best[bin] is not { } current || v > current) best[bin] = v;
        }

        var result = new List<(DateTimeOffset, double?)>(bins);
        for (var i = 0; i < bins; i++) result.Add((from.AddTicks((long)(i * width)), best[i]));
        return result;
    }
}
