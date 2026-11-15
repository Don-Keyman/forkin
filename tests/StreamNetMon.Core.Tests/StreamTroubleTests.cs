namespace StreamNetMon.Core.Tests;

public class StreamTroubleTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

    static StreamSession S(string name, long losses, long recoveries) =>
        new(name, null, T0, new StreamInfo(100_000, 120, losses, recoveries, 0, "HEVC"));

    static StreamTroubleTracker Feed(params (int Sec, StreamSession[] Sessions)[] polls)
    {
        var t = new StreamTroubleTracker();
        foreach (var (sec, sessions) in polls) t.Observe(sessions, T0.AddSeconds(sec));
        return t;
    }

    [Fact]
    public void Steady_counters_are_no_trouble()
    {
        var t = Feed((0, [S("pp", 4, 10)]), (10, [S("pp", 4, 10)]), (20, [S("pp", 4, 10)]));
        Assert.Empty(t.Troubled());
    }

    [Fact]
    public void New_losses_within_the_window_are_trouble()
    {
        var t = Feed((0, [S("pp", 0, 0)]), (10, [S("pp", 2, 0)]));
        Assert.Equal(["pp"], t.Troubled());
    }

    [Fact]
    public void A_single_loss_is_not_enough()
    {
        var t = Feed((0, [S("pp", 0, 0)]), (10, [S("pp", 1, 0)]));
        Assert.Empty(t.Troubled());
    }

    [Fact]
    public void Several_frame_recoveries_within_the_window_are_trouble()
    {
        var t = Feed((0, [S("pp", 0, 5)]), (10, [S("pp", 0, 8)]));
        Assert.Equal(["pp"], t.Troubled());
    }

    [Fact]
    public void Trouble_clears_once_it_is_older_than_the_window()
    {
        var t = Feed((0, [S("pp", 0, 0)]), (2, [S("pp", 5, 0)]), (40, [S("pp", 5, 0)]));
        Assert.Empty(t.Troubled());
    }

    [Fact]
    public void Counters_that_go_backwards_mean_a_new_stream_and_start_over()
    {
        var t = Feed((0, [S("pp", 50, 40)]), (4, [S("pp", 0, 1)]), (8, [S("pp", 1, 2)]));
        Assert.Empty(t.Troubled());
    }

    [Fact]
    public void Ended_streams_are_forgotten()
    {
        var t = Feed((0, [S("pp", 0, 0)]), (4, [S("pp", 9, 0)]), (6, []));
        Assert.Empty(t.Troubled());
    }

    [Fact]
    public void Each_stream_is_judged_on_its_own()
    {
        var t = Feed((0, [S("pp", 0, 0), S("stairwire", 0, 0)]), (6, [S("pp", 0, 0), S("stairwire", 3, 0)]));
        Assert.Equal(["stairwire"], t.Troubled());
    }

    [Fact]
    public void Sessions_without_stream_figures_are_ignored()
    {
        var t = new StreamTroubleTracker();
        t.Observe([new StreamSession("pp", null, T0)], T0);
        Assert.Empty(t.Troubled());
    }
}
