using System.Security.Cryptography;
using System.Text;

namespace StreamNetMon.Core;

public sealed record Snapshot(DateTimeOffset At, Verdict Verdict, AdapterStats? Adapter,
    IReadOnlyList<TargetState> Targets, ServerStatus Server, IReadOnlySet<Guid> SessionDeviceIds,
    IReadOnlySet<Guid> StreamingIds)
{
    public static Snapshot Empty { get; } =
        new(DateTimeOffset.MinValue, Verdict.AllClear, null, [], ServerStatus.Absent, new HashSet<Guid>(), new HashSet<Guid>());
}

/// <summary>Does one second's work per tick: probe, read the adapter, follow sessions, judge, record.</summary>
public sealed class MonitorEngine
{
    public static readonly Guid GatewayId = new("6a7e0000-0000-4000-8000-000000000001");

    private static readonly TimeSpan SessionsEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PruneEvery = TimeSpan.FromDays(1);
    private static readonly TimeSpan BucketSize = TimeSpan.FromSeconds(5);

    private readonly AppSettings _settings;
    private readonly IPinger _pinger;
    private readonly IAdapterSource _adapter;
    private readonly IStreamServer _server;
    private readonly INeighbourSource _neighbours;
    private readonly HistoryStore _history;
    private readonly IClock _clock;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, TargetMonitor> _monitors = new();
    private readonly Dictionary<Guid, BucketAggregator> _targetBuckets = new();
    private readonly BucketAggregator _adapterBuckets = new(BucketSize);
    private readonly AdapterTracker _adapterTracker = new();
    private readonly Dictionary<Guid, Device> _sessionDevices = new();
    private readonly Dictionary<(string Name, string? Address), long> _openSessions = new();

    private readonly HashSet<long> _sessionAddressKnown = new();
    private ServerStatus _serverStatus = ServerStatus.Absent;
    private Task<ServerStatus>? _poll;
    private DateTimeOffset? _polledAt;
    private DateTimeOffset? _prunedAt;
    private int _ticking;

    public MonitorEngine(AppSettings settings, IPinger pinger, IAdapterSource adapter, IStreamServer server,
        INeighbourSource neighbours, HistoryStore history, IClock clock)
    {
        _settings = settings;
        _pinger = pinger;
        _adapter = adapter;
        _server = server;
        _neighbours = neighbours;
        _history = history;
        _clock = clock;
        _history.CloseOpenSessions(clock.Now);
    }

    public Snapshot Current { get; private set; } = Snapshot.Empty;

    public event Action<Snapshot>? Updated;

    /// <summary>One second's work. A call that arrives while another is still running is skipped.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            var now = _clock.Now;
            if (_prunedAt is not { } pruned || now - pruned >= PruneEvery)
            {
                _history.Prune(now);
                _prunedAt = now;
            }

            FollowServer(now, ct);

            List<TargetMonitor> monitors;
            lock (_gate)
            {
                SyncTargets();
                monitors = _monitors.Values.ToList();
            }
            await Task.WhenAll(monitors.Select(m => m.ProbeOnceAsync(ct))).ConfigureAwait(false);

            var adapter = _adapterTracker.Update(_adapter.Read(), now);

            Snapshot snapshot;
            lock (_gate)
            {
                var states = monitors.Where(m => _monitors.ContainsKey(m.Device.Id)).Select(m => m.State())
                    .OrderBy(s => s.Device.Role == TargetRole.StreamingDevice).ThenBy(s => s.Device.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var verdict = VerdictEngine.Evaluate(adapter, states, _settings.Thresholds);
                Record(now, states, adapter);
                snapshot = new Snapshot(now, verdict, adapter, states, _serverStatus, _sessionDevices.Keys.ToHashSet(),
                    FindStreaming(states));
            }

            Current = snapshot;
            Updated?.Invoke(snapshot);
        }
        finally
        {
            Volatile.Write(ref _ticking, 0);
        }
    }

    /// <summary>Call after the saved device list in settings has been edited.</summary>
    public void ReloadDevices()
    {
        lock (_gate) SyncTargets();
    }

    /// <summary>Changes settings safely while the engine is running, then picks up device changes.</summary>
    public void Edit(Action<AppSettings> change)
    {
        lock (_gate)
        {
            change(_settings);
            SyncTargets();
        }
    }

    /// <summary>Turns a device that appeared with a streaming session into a saved one.</summary>
    public Device PinSessionDevice(Guid id, ConnectionType type)
    {
        lock (_gate)
        {
            if (!_sessionDevices.Remove(id, out var device))
                throw new InvalidOperationException("That device is no longer streaming.");
            var pinned = device with { Type = type };
            _settings.Devices.Add(pinned);
            SyncTargets();
            return pinned;
        }
    }

    // --- streaming server -------------------------------------------------------------------

    private void FollowServer(DateTimeOffset now, CancellationToken ct)
    {
        // The poll runs alongside probing and is collected when it finishes, so a slow
        // server can never hold up a tick.
        if (_poll is null && (_polledAt is not { } at || now - at >= SessionsEvery))
        {
            _polledAt = now;
            try { _poll = _server.PollAsync(ct); }
            catch (Exception) { _poll = Task.FromResult(ServerStatus.Absent with { Note = StreamServerLink.UnavailableNote }); }
        }

        if (_poll is not { IsCompleted: true } done) return;
        _poll = null;
        _serverStatus = done.IsCompletedSuccessfully
            ? done.Result
            : ServerStatus.Absent with { Note = StreamServerLink.UnavailableNote };
        _ = done.Exception;   // observed

        lock (_gate) ApplySessions(now, _serverStatus.Sessions);
    }

    private void ApplySessions(DateTimeOffset now, IReadOnlyList<StreamSession> sessions)
    {
        // A session is the same session for as long as the same client is connected from the
        // same address, whatever else the server reports about it from one poll to the next.
        var live = sessions.DistinctBy(Key).ToList();
        var liveKeys = live.Select(Key).ToHashSet();

        foreach (var ended in _openSessions.Keys.Where(k => !liveKeys.Contains(k)).ToList())
        {
            _history.CloseSession(_openSessions[ended], now);
            _openSessions.Remove(ended);
        }

        foreach (var started in live.Where(s => !_openSessions.ContainsKey(Key(s))))
        {
            _openSessions[Key(started)] = _history.OpenSession(started.ClientName, started.Address, started.StartedAt ?? now);
        }

        // Session devices: one per live address that no saved device already covers.
        var wanted = new Dictionary<Guid, Device>();
        foreach (var s in live)
        {
            if (s.Address is not { } address || SavedByHost(address) is not null) continue;
            var id = SessionDeviceId(address);
            wanted.TryAdd(id, new Device(id, s.ClientName, address, ConnectionType.WiFi, TargetRole.StreamingDevice));
        }
        _sessionDevices.Clear();
        foreach (var (id, device) in wanted) _sessionDevices[id] = device;
    }

    private static (string Name, string? Address) Key(StreamSession s) =>
        (s.ClientName.ToLowerInvariant(), s.Address?.ToLowerInvariant());

    private Device? SavedByHost(string host) =>
        _settings.Devices.FirstOrDefault(d => string.Equals(d.Host, host, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Which targets the live sessions are going to. A session with an address is matched by it.
    /// Without one (Vibepollo reports only the client's name) it is matched to the saved device
    /// of that name; when several share it ("pp wired", "pp Wi-Fi"), to the one that is online.
    /// </summary>
    private HashSet<Guid> FindStreaming(List<TargetState> states)
    {
        var streaming = new HashSet<Guid>(_sessionDevices.Keys);
        foreach (var (key, sessionId) in _openSessions)
        {
            Device? device;
            if (key.Address is { } address)
            {
                device = SavedByHost(address);
            }
            else
            {
                var named = _settings.Devices.Where(d => NamedFor(d.Name, key.Name)).ToList();
                device = named.Count == 1
                    ? named[0]
                    : named.FirstOrDefault(d => states.Any(s => s.Device.Id == d.Id && !s.Stats.Offline));
                if (device is not null && _sessionAddressKnown.Add(sessionId))
                    _history.SetSessionAddress(sessionId, device.Host);
            }
            if (device is not null) streaming.Add(device.Id);
        }
        return streaming;
    }

    /// <summary>"pp", "pp wired" and "pp-wifi" are all devices for the client named "pp"; "Apple TV" is not.</summary>
    private static bool NamedFor(string deviceName, string clientName)
    {
        if (!deviceName.StartsWith(clientName, StringComparison.OrdinalIgnoreCase)) return false;
        return deviceName.Length == clientName.Length || " -_(".Contains(deviceName[clientName.Length]);
    }

    /// <summary>The same address always gets the same id, so its history lines up across sessions.</summary>
    private static Guid SessionDeviceId(string address) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes("session:" + address.ToLowerInvariant())));

    // --- targets ----------------------------------------------------------------------------

    private void SyncTargets()
    {
        var wanted = new Dictionary<Guid, Device>();
        foreach (var d in _settings.Devices) wanted.TryAdd(d.Id, d);
        if (_neighbours.DefaultGateway() is { Length: > 0 } gateway && SavedByHost(gateway) is null)
            wanted.TryAdd(GatewayId, new Device(GatewayId, "Gateway", gateway, ConnectionType.Wired, TargetRole.Gateway));
        foreach (var (id, d) in _sessionDevices)
            if (SavedByHost(d.Host) is null) wanted.TryAdd(id, d);

        foreach (var id in _monitors.Keys.Except(wanted.Keys).ToList())
        {
            _monitors.Remove(id);
            _targetBuckets.Remove(id);
        }

        foreach (var (id, device) in wanted)
        {
            if (_monitors.TryGetValue(id, out var existing) && existing.Device.Host == device.Host)
                existing.Device = device;
            else
                _monitors[id] = new TargetMonitor(device, _pinger, _clock);
        }
    }

    // --- history ----------------------------------------------------------------------------

    private void Record(DateTimeOffset now, List<TargetState> states, AdapterStats? adapter)
    {
        var targets = new List<(Guid Id, TargetBucket Bucket)>();
        foreach (var s in states)
        {
            if (!_targetBuckets.TryGetValue(s.Device.Id, out var buckets))
                _targetBuckets[s.Device.Id] = buckets = new BucketAggregator(BucketSize);
            if (buckets.Add(now, s.Stats) is { } done) targets.Add((s.Device.Id, done));
        }
        var adapterBucket = adapter is null ? null : _adapterBuckets.Add(now, adapter);
        if (targets.Count == 0 && adapterBucket is null) return;

        _history.Batch(() =>
        {
            foreach (var (id, bucket) in targets) _history.AppendTarget(id, bucket);
            if (adapterBucket is not null) _history.AppendAdapter(adapterBucket);
        });
    }
}
