namespace StreamNetMon.Core.Tests;

public class DownsampleTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Few_points_pass_through_unchanged()
    {
        (DateTimeOffset, double?)[] points = [(T0, 1), (T0.AddSeconds(5), null), (T0.AddSeconds(10), 3)];
        Assert.Equal(points, Downsample.Max(points, T0, T0.AddSeconds(10), 600));
    }

    [Fact]
    public void Many_points_collapse_to_the_worst_value_per_bin()
    {
        var points = Enumerable.Range(0, 1000).Select(i => (T0.AddSeconds(i), (double?)(i % 100))).ToList();
        var result = Downsample.Max(points, T0, T0.AddSeconds(1000), 10);
        Assert.Equal(10, result.Count);
        Assert.All(result, p => Assert.Equal(99, p.Value));
        Assert.Equal(T0, result[0].At);
    }

    [Fact]
    public void A_bin_with_no_values_stays_a_gap()
    {
        var points = Enumerable.Range(0, 1000)
            .Select(i => (T0.AddSeconds(i), i is >= 100 and < 200 ? null : (double?)1)).ToList();
        var result = Downsample.Max(points, T0, T0.AddSeconds(1000), 10);
        Assert.Null(result[1].Value);
        Assert.Equal(1, result[2].Value);
    }

    [Fact]
    public void An_empty_stretch_between_data_is_a_gap_not_a_line()
    {
        var points = Enumerable.Range(0, 300).Select(i => (T0.AddSeconds(i), (double?)1))
            .Concat(Enumerable.Range(700, 300).Select(i => (T0.AddSeconds(i), (double?)1))).ToList();
        var result = Downsample.Max(points, T0, T0.AddSeconds(1000), 10);
        Assert.Contains(result, p => p.Value is null);
    }

    [Fact]
    public void Empty_input_gives_empty_output() =>
        Assert.Empty(Downsample.Max([], T0, T0.AddHours(1), 600));
}
