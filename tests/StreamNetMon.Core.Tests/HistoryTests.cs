namespace StreamNetMon.Core.Tests;

public sealed class HistoryTests : IDisposable
{
    static readonly DateTimeOffset T0 = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snm-" + Guid.NewGuid().ToString("N"));
    private readonly string _db;

    public HistoryTests()
    {
        Directory.CreateDirectory(_dir);
        _db = Path.Combine(_dir, "history.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    static TargetStats S(double latency, double jitter = 1, double loss = 0) => new(latency, jitter, loss, false, 30);
    static TargetBucket B(DateTimeOffset at, double avg = 5) => new(at, avg - 1, avg, avg + 1, 0.5, 0, false);

    [Fact]
    public void Aggregator_emits_bucket_every_5s_with_min_avg_max()
    {
        var a = new BucketAggregator(TimeSpan.FromSeconds(5));
        TargetBucket? done = null;
        double[] latencies = [10, 20, 30, 40, 50];
        for (var i = 0; i < 5; i++) Assert.Null(a.Add(T0.AddSeconds(i), S(latencies[i], jitter: i, loss: i == 4 ? 10 : 0)));
        done = a.Add(T0.AddSeconds(5), S(99));
        Assert.NotNull(done);
        Assert.Equal(T0, done.Start);
        Assert.Equal(10, done.MinMs);
        Assert.Equal(30, done.AvgMs);
        Assert.Equal(50, done.MaxMs);
        Assert.Equal(2, done.JitterMs);
        Assert.Equal(10, done.LossPct);
        Assert.False(done.Offline);
    }

    [Fact]
    public void Aggregator_bucket_start_is_aligned_to_5s()
    {
        var a = new BucketAggregator(TimeSpan.FromSeconds(5));
        a.Add(T0.AddSeconds(3.2), S(10));
        var done = a.Add(T0.AddSeconds(5.1), S(10));
        Assert.Equal(T0, done!.Start);
    }

    [Fact]
    public void Aggregator_offline_bucket_has_no_latency()
    {
        var a = new BucketAggregator(TimeSpan.FromSeconds(5));
        a.Add(T0, new TargetStats(null, null, 0, true, 0));
        var done = a.Add(T0.AddSeconds(5), S(10));
        Assert.True(done!.Offline);
        Assert.Null(done.AvgMs);
    }

    [Fact]
    public void Aggregator_adapter_bucket_averages_rates_and_sums_errors()
    {
        var a = new BucketAggregator(TimeSpan.FromSeconds(5));
        a.Add(T0, new AdapterStats("Ethernet", 2_500_000_000, 2_500_000_000, 10, 100, 1, 2));
        a.Add(T0.AddSeconds(1), new AdapterStats("Ethernet", 2_500_000_000, 2_500_000_000, 30, 300, 2, 0));
        var done = a.Add(T0.AddSeconds(5), new AdapterStats("Ethernet", 2_500_000_000, 2_500_000_000, 0, 0, 0, 0));
        Assert.Equal(new AdapterBucket(T0, 20, 200, 3, 2, 2_500_000_000), done);
    }

    [Fact]
    public void Append_then_query_round_trips()
    {
        var id = Guid.NewGuid();
        using var h = new HistoryStore(_db);
        var bucket = new TargetBucket(T0, 1.5, 2.5, 9, 0.4, 3.3, false);
        h.AppendTarget(id, bucket);
        h.AppendTarget(Guid.NewGuid(), B(T0));
        h.AppendTarget(id, new TargetBucket(T0.AddSeconds(5), null, null, null, null, 0, true));
        var rows = h.QueryTarget(id, T0.AddMinutes(-1), T0.AddMinutes(1));
        Assert.Equal(2, rows.Count);
        Assert.Equal(bucket, rows[0]);
        Assert.True(rows[1].Offline);
        Assert.Null(rows[1].AvgMs);
    }

    [Fact]
    public void Adapter_round_trips()
    {
        using var h = new HistoryStore(_db);
        var b = new AdapterBucket(T0, 38_200_000, 1_000, 1, 2, 2_500_000_000);
        h.AppendAdapter(b);
        Assert.Equal(b, Assert.Single(h.QueryAdapter(T0, T0.AddSeconds(1))));
    }

    [Fact]
    public void Query_respects_range()
    {
        var id = Guid.NewGuid();
        using var h = new HistoryStore(_db);
        for (var i = 0; i < 10; i++) h.AppendTarget(id, B(T0.AddSeconds(i * 5)));
        Assert.Equal(3, h.QueryTarget(id, T0.AddSeconds(10), T0.AddSeconds(20)).Count);
    }

    [Fact]
    public void Prune_removes_only_rows_older_than_14_days()
    {
        var id = Guid.NewGuid();
        using var h = new HistoryStore(_db);
        h.AppendTarget(id, B(T0.AddDays(-15)));
        h.AppendTarget(id, B(T0.AddDays(-13)));
        h.AppendAdapter(new AdapterBucket(T0.AddDays(-20), 1, 1, 0, 0, 1));
        var old = h.OpenSession("old", null, T0.AddDays(-16));
        h.CloseSession(old, T0.AddDays(-15.9));
        h.OpenSession("recent", null, T0.AddDays(-1));
        Assert.Equal(3, h.Prune(T0));
        Assert.Single(h.QueryTarget(id, T0.AddDays(-30), T0));
        Assert.Empty(h.QueryAdapter(T0.AddDays(-30), T0));
        Assert.Equal("recent", Assert.Single(h.QuerySessions(T0.AddDays(-30), T0)).ClientName);
    }

    [Fact]
    public void Session_open_close_round_trips()
    {
        using var h = new HistoryStore(_db);
        var id = h.OpenSession("iPad", "192.168.1.40", T0);
        var open = Assert.Single(h.QuerySessions(T0.AddMinutes(-1), T0.AddMinutes(1)));
        Assert.Equal(new SessionRecord(id, "iPad", "192.168.1.40", T0, null), open);
        h.CloseSession(id, T0.AddMinutes(30));
        Assert.Equal(T0.AddMinutes(30), h.QuerySessions(T0, T0.AddHours(1))[0].End);
    }

    [Fact]
    public void Sessions_overlapping_the_range_are_returned()
    {
        using var h = new HistoryStore(_db);
        var id = h.OpenSession("iPad", null, T0);
        h.CloseSession(id, T0.AddHours(2));
        Assert.Single(h.QuerySessions(T0.AddHours(1), T0.AddHours(1.5)));
        Assert.Empty(h.QuerySessions(T0.AddHours(3), T0.AddHours(4)));
    }

    [Fact]
    public void CloseOpenSessions_closes_leftovers()
    {
        using var h = new HistoryStore(_db);
        h.OpenSession("iPad", null, T0);
        h.CloseOpenSessions(T0.AddMinutes(5));
        Assert.Equal(T0.AddMinutes(5), h.QuerySessions(T0, T0.AddHours(1))[0].End);
    }

    [Fact]
    public void Reopening_existing_db_keeps_data()
    {
        var id = Guid.NewGuid();
        using (var h = new HistoryStore(_db)) h.AppendTarget(id, B(T0));
        using var again = new HistoryStore(_db);
        Assert.Single(again.QueryTarget(id, T0, T0.AddSeconds(1)));
    }

    [Fact]
    public void Batch_writes_everything_in_one_transaction()
    {
        var id = Guid.NewGuid();
        using var h = new HistoryStore(_db);
        h.Batch(() =>
        {
            h.AppendTarget(id, B(T0));
            h.AppendAdapter(new AdapterBucket(T0, 1, 1, 0, 0, 1));
        });
        Assert.Single(h.QueryTarget(id, T0, T0));
        Assert.Single(h.QueryAdapter(T0, T0));
    }
}
