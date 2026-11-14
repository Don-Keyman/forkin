namespace StreamNetMon.Core.Tests;

public class RollingWindowTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    static RollingWindow New() => new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));

    static RollingWindow With(params double?[] rtts)
    {
        var w = New();
        for (var i = 0; i < rtts.Length; i++) w.Add(new ProbeResult(T0.AddSeconds(i), rtts[i]));
        return w;
    }

    [Fact]
    public void Latency_is_mean_of_replies() =>
        Assert.Equal(20, With(10, 20, 30).Snapshot(T0.AddSeconds(3)).LatencyMs);

    [Fact]
    public void Jitter_is_mean_abs_consecutive_diff() =>
        Assert.Equal(3.5, With(10, 14, 11).Snapshot(T0.AddSeconds(3)).JitterMs);

    [Fact]
    public void Jitter_is_null_with_one_reply() =>
        Assert.Null(With(10).Snapshot(T0.AddSeconds(1)).JitterMs);

    [Fact]
    public void Loss_counts_missing_replies() =>
        Assert.Equal(25.0, With(10, 10, null, 10).Snapshot(T0.AddSeconds(4)).LossPct);

    [Fact]
    public void Old_samples_leave_the_window()
    {
        var s = With(10).Snapshot(T0.AddSeconds(31));
        Assert.Equal(0, s.Samples);
    }

    [Fact]
    public void Offline_after_60s_without_reply()
    {
        var w = New();
        w.Add(new ProbeResult(T0, 10));
        for (var i = 1; i <= 61; i++) w.Add(new ProbeResult(T0.AddSeconds(i), null));
        var s = w.Snapshot(T0.AddSeconds(61));
        Assert.True(s.Offline);
        Assert.Equal(0, s.LossPct);
        Assert.Null(s.LatencyMs);
        Assert.Null(s.JitterMs);
    }

    [Fact]
    public void Not_offline_while_last_reply_is_recent()
    {
        var w = New();
        w.Add(new ProbeResult(T0, 10));
        for (var i = 1; i <= 40; i++) w.Add(new ProbeResult(T0.AddSeconds(i), null));
        var s = w.Snapshot(T0.AddSeconds(40));
        Assert.False(s.Offline);
        Assert.Equal(100, s.LossPct);
    }

    [Fact]
    public void Never_replied_is_offline()
    {
        Assert.True(New().Snapshot(T0).Offline);
        Assert.True(With(null, null).Snapshot(T0.AddSeconds(2)).Offline);
    }

    [Fact]
    public void Gap_in_samples_does_not_count_as_loss()
    {
        var w = New();
        w.Add(new ProbeResult(T0, 10));
        w.Add(new ProbeResult(T0.AddSeconds(20), 12));
        var s = w.Snapshot(T0.AddSeconds(20));
        Assert.Equal(0, s.LossPct);
        Assert.Equal(2, s.Samples);
    }

    [Fact]
    public void Recent_returns_samples_within_span()
    {
        var w = With(10, 11, 12, 13);
        Assert.Equal(2, w.Recent(T0.AddSeconds(3), TimeSpan.FromSeconds(1.5)).Count);
    }

    [Fact]
    public void EverReplied_remembers_a_reply_after_going_offline()
    {
        var w = New();
        Assert.False(w.Snapshot(T0).EverReplied);
        w.Add(new ProbeResult(T0, 10));
        Assert.True(w.Snapshot(T0).EverReplied);
        var s = w.Snapshot(T0.AddSeconds(120));
        Assert.True(s.Offline);
        Assert.True(s.EverReplied);
    }
}
