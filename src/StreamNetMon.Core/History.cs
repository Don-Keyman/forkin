using Microsoft.Data.Sqlite;

namespace StreamNetMon.Core;

public sealed record TargetBucket(DateTimeOffset Start, double? MinMs, double? AvgMs, double? MaxMs, double? JitterMs, double LossPct, bool Offline);

public sealed record AdapterBucket(DateTimeOffset Start, double SendBps, double ReceiveBps, long OutErrors, long OutDiscards, long LinkSpeedBps);

public sealed record SessionRecord(long Id, string ClientName, string? Address, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>
/// Collapses once-a-second readings into fixed-size buckets. Use one instance per series
/// (one per target, one for the adapter).
/// </summary>
public sealed class BucketAggregator(TimeSpan size)
{
    private DateTimeOffset? _start;
    private readonly List<TargetStats> _targets = [];
    private readonly List<AdapterStats> _adapters = [];

    /// <summary>Returns the finished bucket when <paramref name="now"/> has moved into a new one.</summary>
    public TargetBucket? Add(DateTimeOffset now, TargetStats s)
    {
        var finished = Roll(now) is { } start && _targets.Count > 0 ? Finish(start, _targets) : null;
        if (finished is not null || _start != Align(now)) { _targets.Clear(); _start = Align(now); }
        _targets.Add(s);
        return finished;
    }

    public AdapterBucket? Add(DateTimeOffset now, AdapterStats s)
    {
        var finished = Roll(now) is { } start && _adapters.Count > 0 ? Finish(start, _adapters) : null;
        if (finished is not null || _start != Align(now)) { _adapters.Clear(); _start = Align(now); }
        _adapters.Add(s);
        return finished;
    }

    private DateTimeOffset? Roll(DateTimeOffset now) => _start is { } s && Align(now) != s ? s : null;

    private DateTimeOffset Align(DateTimeOffset t) =>
        new(t.UtcTicks - t.UtcTicks % size.Ticks, TimeSpan.Zero);

    private static TargetBucket Finish(DateTimeOffset start, List<TargetStats> samples)
    {
        var latencies = samples.Where(s => s.LatencyMs is not null).Select(s => s.LatencyMs!.Value).ToList();
        var jitters = samples.Where(s => s.JitterMs is not null).Select(s => s.JitterMs!.Value).ToList();
        return new TargetBucket(start,
            latencies.Count > 0 ? latencies.Min() : null,
            latencies.Count > 0 ? latencies.Average() : null,
            latencies.Count > 0 ? latencies.Max() : null,
            jitters.Count > 0 ? jitters.Average() : null,
            samples.Max(s => s.LossPct),
            samples.All(s => s.Offline));
    }

    private static AdapterBucket Finish(DateTimeOffset start, List<AdapterStats> samples) =>
        new(start, samples.Average(s => s.SendBps), samples.Average(s => s.ReceiveBps),
            samples.Sum(s => s.OutErrorsDelta), samples.Sum(s => s.OutDiscardsDelta), samples[^1].LinkSpeedBps);
}

/// <summary>SQLite-backed history of target, adapter and session data.</summary>
public sealed class HistoryStore : IDisposable
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    public HistoryStore(string dbPath)
    {
        _db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        _db.Open();
        Execute("PRAGMA journal_mode=WAL");
        Execute("PRAGMA synchronous=NORMAL");
        Execute("""
            CREATE TABLE IF NOT EXISTS target (
                device TEXT NOT NULL, at INTEGER NOT NULL,
                min_ms REAL, avg_ms REAL, max_ms REAL, jitter_ms REAL, loss REAL NOT NULL, offline INTEGER NOT NULL,
                PRIMARY KEY (device, at)) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS adapter (
                at INTEGER PRIMARY KEY, send REAL NOT NULL, receive REAL NOT NULL,
                errors INTEGER NOT NULL, discards INTEGER NOT NULL, speed INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS session (
                id INTEGER PRIMARY KEY AUTOINCREMENT, client TEXT NOT NULL, address TEXT,
                start INTEGER NOT NULL, end INTEGER);
            """);
    }

    public void AppendTarget(Guid deviceId, TargetBucket b) => Execute(
        "INSERT OR REPLACE INTO target VALUES ($d,$at,$min,$avg,$max,$jit,$loss,$off)",
        ("$d", deviceId.ToString()), ("$at", Ms(b.Start)), ("$min", b.MinMs), ("$avg", b.AvgMs), ("$max", b.MaxMs),
        ("$jit", b.JitterMs), ("$loss", b.LossPct), ("$off", b.Offline ? 1 : 0));

    public void AppendAdapter(AdapterBucket b) => Execute(
        "INSERT OR REPLACE INTO adapter VALUES ($at,$s,$r,$e,$d,$sp)",
        ("$at", Ms(b.Start)), ("$s", b.SendBps), ("$r", b.ReceiveBps), ("$e", b.OutErrors), ("$d", b.OutDiscards), ("$sp", b.LinkSpeedBps));

    public long OpenSession(string clientName, string? address, DateTimeOffset start)
    {
        lock (_gate)
        {
            using var cmd = Command("INSERT INTO session (client,address,start) VALUES ($c,$a,$s); SELECT last_insert_rowid()",
                ("$c", clientName), ("$a", address), ("$s", Ms(start)));
            return (long)cmd.ExecuteScalar()!;
        }
    }

    public void SetSessionAddress(long id, string address) =>
        Execute("UPDATE session SET address=$a WHERE id=$id", ("$a", address), ("$id", id));

    public void CloseSession(long id, DateTimeOffset end) =>
        Execute("UPDATE session SET end=$e WHERE id=$id", ("$e", Ms(end)), ("$id", id));

    /// <summary>Closes sessions left open by a previous run that ended without closing them.</summary>
    public void CloseOpenSessions(DateTimeOffset end) =>
        Execute("UPDATE session SET end=MAX($e, start) WHERE end IS NULL", ("$e", Ms(end)));

    public IReadOnlyList<TargetBucket> QueryTarget(Guid deviceId, DateTimeOffset from, DateTimeOffset to) => Query(
        "SELECT at,min_ms,avg_ms,max_ms,jitter_ms,loss,offline FROM target WHERE device=$d AND at>=$f AND at<=$t ORDER BY at",
        r => new TargetBucket(At(r.GetInt64(0)), Real(r, 1), Real(r, 2), Real(r, 3), Real(r, 4), r.GetDouble(5), r.GetInt64(6) != 0),
        ("$d", deviceId.ToString()), ("$f", Ms(from)), ("$t", Ms(to)));

    public IReadOnlyList<AdapterBucket> QueryAdapter(DateTimeOffset from, DateTimeOffset to) => Query(
        "SELECT at,send,receive,errors,discards,speed FROM adapter WHERE at>=$f AND at<=$t ORDER BY at",
        r => new AdapterBucket(At(r.GetInt64(0)), r.GetDouble(1), r.GetDouble(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5)),
        ("$f", Ms(from)), ("$t", Ms(to)));

    /// <summary>Sessions that overlap the range; an open session counts as still running.</summary>
    public IReadOnlyList<SessionRecord> QuerySessions(DateTimeOffset from, DateTimeOffset to) => Query(
        "SELECT id,client,address,start,end FROM session WHERE start<=$t AND (end IS NULL OR end>=$f) ORDER BY start",
        r => new SessionRecord(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), At(r.GetInt64(3)),
            r.IsDBNull(4) ? null : At(r.GetInt64(4))),
        ("$f", Ms(from)), ("$t", Ms(to)));

    /// <summary>Deletes everything older than the retention period. Returns the number of rows removed.</summary>
    public int Prune(DateTimeOffset now)
    {
        var cutoff = Ms(now - Retention);
        return Execute("DELETE FROM target WHERE at<$c", ("$c", cutoff))
             + Execute("DELETE FROM adapter WHERE at<$c", ("$c", cutoff))
             + Execute("DELETE FROM session WHERE start<$c AND end IS NOT NULL AND end<$c", ("$c", cutoff));
    }

    /// <summary>Runs several writes as one transaction.</summary>
    public void Batch(Action writes)
    {
        lock (_gate)
        {
            using var tx = _db.BeginTransaction();
            writes();
            tx.Commit();
        }
    }

    public void Dispose()
    {
        lock (_gate) _db.Dispose();
    }

    private static long Ms(DateTimeOffset t) => t.ToUnixTimeMilliseconds();
    private static DateTimeOffset At(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms);
    private static double? Real(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private int Execute(string sql, params (string Name, object? Value)[] args)
    {
        lock (_gate)
        {
            using var cmd = Command(sql, args);
            return cmd.ExecuteNonQuery();
        }
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        lock (_gate)
        {
            using var cmd = Command(sql, args);
            using var reader = cmd.ExecuteReader();
            var rows = new List<T>();
            while (reader.Read()) rows.Add(map(reader));
            return rows;
        }
    }
}
