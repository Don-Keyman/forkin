namespace StreamNetMon.Core.Tests;

public sealed class MonitorEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();
    private readonly FakePinger _pinger = new();
    private readonly FakeAdapter _adapter = new();
    private readonly FakeServer _server = new();
    private readonly FakeNeighbours _neighbours = new();
    private readonly AppSettings _settings = new();
    private readonly HistoryStore _history;

    public MonitorEngineTests()
    {
        Directory.CreateDirectory(_dir);
        _history = new HistoryStore(Path.Combine(_dir, "history.db"));
    }

    public void Dispose()
    {
        _history.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, true);
    }

    private MonitorEngine Engine() => new(_settings, _pinger, _adapter, _server, _neighbours, _history, _clock);

    private async Task Ticks(MonitorEngine e, int n)
    {
        for (var i = 0; i < n; i++)
        {
            await e.TickAsync(default);
            _clock.Advance(1);
        }
    }

    private Device Saved(string name, string host, ConnectionType type = ConnectionType.Wired, TargetRole role = TargetRole.StreamingDevice)
    {
        var d = new Device(Guid.NewGuid(), name, host, type, role);
        _settings.Devices.Add(d);
        return d;
    }

    static StreamSession Session(string name, string? address) => new(name, address, null);

    [Fact]
    public async Task Gateway_is_added_automatically()
    {
        var e = Engine();
        await Ticks(e, 1);
        var t = Assert.Single(e.Current.Targets);
        Assert.Equal(new Device(MonitorEngine.GatewayId, "Gateway", "192.168.1.1", ConnectionType.Wired, TargetRole.Gateway), t.Device);
    }

    [Fact]
    public async Task Gateway_not_duplicated_when_saved()
    {
        Saved("Router", "192.168.1.1", role: TargetRole.Router);
        var e = Engine();
        await Ticks(e, 1);
        Assert.Equal("Router", Assert.Single(e.Current.Targets).Device.Name);
    }

    [Fact]
    public async Task No_gateway_target_when_there_is_no_gateway()
    {
        _neighbours.Gateway = null;
        var e = Engine();
        await Ticks(e, 1);
        Assert.Empty(e.Current.Targets);
    }

    [Fact]
    public async Task Saved_devices_are_probed_every_tick()
    {
        Saved("iPad wired", "192.168.1.40");
        _pinger.Hosts["192.168.1.40"] = () => 0.9;
        var e = Engine();
        await Ticks(e, 3);
        var t = e.Current.Targets.Single(x => x.Device.Name == "iPad wired");
        Assert.Equal(3, t.Stats.Samples);
        Assert.Equal(0.9, t.Stats.LatencyMs);
    }

    [Fact]
    public async Task Session_adds_device_and_end_removes_it()
    {
        var e = Engine();
        _server.Streaming(Session("iPad", "192.168.1.40"));
        await Ticks(e, 1);
        var t = e.Current.Targets.Single(x => x.Device.Host == "192.168.1.40");
        Assert.Equal("iPad", t.Device.Name);
        Assert.Equal(ConnectionType.WiFi, t.Device.Type);
        Assert.Equal(TargetRole.StreamingDevice, t.Device.Role);
        Assert.Contains(t.Device.Id, e.Current.SessionDeviceIds);

        _server.Streaming();
        await Ticks(e, 2);
        Assert.DoesNotContain(e.Current.Targets, x => x.Device.Host == "192.168.1.40");
        Assert.Empty(e.Current.SessionDeviceIds);
    }

    [Fact]
    public async Task Session_device_keeps_the_same_id_across_sessions()
    {
        var e = Engine();
        _server.Streaming(Session("iPad", "192.168.1.40"));
        await Ticks(e, 2);
        var first = e.Current.SessionDeviceIds.Single();
        _server.Streaming();
        await Ticks(e, 2);
        _server.Streaming(Session("iPad", "192.168.1.40"));
        await Ticks(e, 2);
        Assert.Equal(first, e.Current.SessionDeviceIds.Single());
    }

    [Fact]
    public async Task Session_matching_saved_address_is_not_duplicated()
    {
        Saved("iPad wired", "192.168.1.40");
        var e = Engine();
        _server.Streaming(Session("iPad", "192.168.1.40"));
        await Ticks(e, 1);
        Assert.Single(e.Current.Targets, x => x.Device.Host == "192.168.1.40");
        Assert.Empty(e.Current.SessionDeviceIds);
    }

    [Fact]
    public async Task Session_without_address_matches_by_name()
    {
        Saved("iPad", "192.168.1.40");
        var e = Engine();
        _server.Streaming(Session("ipad", null), Session("Mystery", null));
        await Ticks(e, 1);
        Assert.Equal(2, e.Current.Targets.Count);   // gateway + saved iPad, nothing for Mystery
        Assert.Empty(e.Current.SessionDeviceIds);
        var sessions = _history.QuerySessions(_clock.Now.AddHours(-1), _clock.Now.AddHours(1));
        Assert.Equal(2, sessions.Count);
        Assert.Equal("192.168.1.40", sessions.Single(s => s.ClientName == "ipad").Address);
        Assert.Null(sessions.Single(s => s.ClientName == "Mystery").Address);
    }

    [Fact]
    public async Task Session_start_and_end_written_to_history()
    {
        var e = Engine();
        var start = _clock.Now;
        _server.Streaming(Session("iPad", "192.168.1.40"));
        await Ticks(e, 4);
        var open = Assert.Single(_history.QuerySessions(start.AddHours(-1), start.AddHours(1)));
        Assert.Equal(("iPad", "192.168.1.40", start), (open.ClientName, open.Address, open.Start));
        Assert.Null(open.End);

        _server.Streaming();
        await Ticks(e, 2);
        var closed = Assert.Single(_history.QuerySessions(start.AddHours(-1), start.AddHours(1)));
        Assert.Equal(start.AddSeconds(4), closed.End);
    }

    [Fact]
    public async Task Sessions_left_open_by_a_previous_run_are_closed_at_start()
    {
        _history.OpenSession("iPad", null, _clock.Now.AddHours(-2));
        Engine();
        Assert.NotNull(Assert.Single(_history.QuerySessions(_clock.Now.AddDays(-1), _clock.Now)).End);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Server_is_polled_every_2s()
    {
        var e = Engine();
        await Ticks(e, 5);   // t = 0,1,2,3,4
        Assert.Equal(3, _server.Polls);
    }

    [Fact]
    public async Task Server_failure_does_not_stop_probing()
    {
        Saved("iPad wired", "192.168.1.40");
        _pinger.Hosts["192.168.1.40"] = () => 1.0;
        _server.Throw = true;
        var e = Engine();
        await Ticks(e, 3);
        Assert.Equal(3, e.Current.Targets.Single(x => x.Device.Name == "iPad wired").Stats.Samples);
        Assert.Equal(StreamServerLink.UnavailableNote, e.Current.Server.Note);
    }

    [Fact]
    public async Task Pin_moves_session_device_into_settings()
    {
        var e = Engine();
        _server.Streaming(Session("iPad", "192.168.1.40"));
        await Ticks(e, 2);
        var id = e.Current.SessionDeviceIds.Single();

        var pinned = e.PinSessionDevice(id, ConnectionType.Wired);

        Assert.Equal(new Device(id, "iPad", "192.168.1.40", ConnectionType.Wired, TargetRole.StreamingDevice), pinned);
        Assert.Equal(pinned, Assert.Single(_settings.Devices));
        _server.Streaming();
        await Ticks(e, 2);
        var kept = e.Current.Targets.Single(x => x.Device.Id == id);
        Assert.Equal(ConnectionType.Wired, kept.Device.Type);
        Assert.Empty(e.Current.SessionDeviceIds);
    }

    [Fact]
    public async Task Reload_picks_up_added_edited_and_removed_devices()
    {
        var a = Saved("A", "10.0.0.1");
        var b = Saved("B", "10.0.0.2");
        var e = Engine();
        await Ticks(e, 1);
        _settings.Devices.Remove(a);
        _settings.Devices[0] = b with { Host = "10.0.0.3" };
        Saved("C", "10.0.0.4");
        e.ReloadDevices();
        await Ticks(e, 1);
        Assert.Equal(["10.0.0.3", "10.0.0.4", "192.168.1.1"], e.Current.Targets.Select(t => t.Device.Host).Order());
        Assert.Equal(1, e.Current.Targets.Single(t => t.Device.Host == "10.0.0.3").Stats.Samples);
    }

    [Fact]
    public async Task Verdict_reflects_degraded_device()
    {
        Saved("iPad Wi-Fi", "192.168.1.40", ConnectionType.WiFi);
        _pinger.Hosts["192.168.1.1"] = () => 0.4;
        var n = 0;
        _pinger.Hosts["192.168.1.40"] = () => n++ % 2 == 0 ? 5.0 : null;
        var e = Engine();
        await Ticks(e, 10);
        Assert.Equal("Wi-Fi hop to iPad Wi-Fi is degraded", e.Current.Verdict.Message);
    }

    [Fact]
    public async Task Buckets_written_every_5s()
    {
        var d = Saved("iPad wired", "192.168.1.40");
        _pinger.Hosts["192.168.1.40"] = () => 1.0;
        var e = Engine();
        var start = _clock.Now;
        await Ticks(e, 11);
        Assert.Equal(2, _history.QueryTarget(d.Id, start, start.AddMinutes(1)).Count);
        Assert.Equal(2, _history.QueryAdapter(start, start.AddMinutes(1)).Count);
    }

    [Fact]
    public async Task Old_history_is_pruned_at_start_and_daily()
    {
        var id = Guid.NewGuid();
        _history.AppendTarget(id, new TargetBucket(_clock.Now.AddDays(-15), 1, 1, 1, 0, 0, false));
        var e = Engine();
        await Ticks(e, 1);
        Assert.Empty(_history.QueryTarget(id, _clock.Now.AddDays(-30), _clock.Now));

        _history.AppendTarget(id, new TargetBucket(_clock.Now.AddDays(-13.5), 1, 1, 1, 0, 0, false));
        _clock.Advance(86_400);
        await Ticks(e, 1);
        Assert.Empty(_history.QueryTarget(id, _clock.Now.AddDays(-30), _clock.Now));
    }

    [Fact]
    public async Task Updated_raised_each_tick()
    {
        var e = Engine();
        var seen = new List<Snapshot>();
        e.Updated += seen.Add;
        await Ticks(e, 3);
        Assert.Equal(3, seen.Count);
        Assert.Same(e.Current, seen[^1]);
    }

    [Fact]
    public async Task Edit_applies_the_change_and_reloads_targets()
    {
        var e = Engine();
        await Ticks(e, 1);
        e.Edit(s => s.Devices.Add(new Device(Guid.NewGuid(), "Shield", "10.0.0.7", ConnectionType.Wired, TargetRole.StreamingDevice)));
        await Ticks(e, 1);
        Assert.Contains(e.Current.Targets, t => t.Device.Name == "Shield");
        Assert.Single(_settings.Devices);
    }

    [Fact]
    public async Task Session_whose_reported_start_changes_is_still_one_session()
    {
        var e = Engine();
        var start = _clock.Now;
        for (var i = 0; i < 6; i++)
        {
            _server.Streaming(new StreamSession("iPad", "192.168.1.40", _clock.Now.AddMilliseconds(-i * 300)));
            await Ticks(e, 1);
        }
        Assert.Single(_history.QuerySessions(start.AddHours(-1), start.AddHours(1)));
    }

    [Fact]
    public async Task Session_without_address_picks_the_saved_variant_that_is_online()
    {
        var wired = Saved("pp wired", "10.0.0.1");
        var wifi = Saved("pp Wi-Fi", "10.0.0.2", ConnectionType.WiFi);
        Saved("Apple TV", "10.0.0.3");
        _pinger.Hosts["10.0.0.2"] = () => 3.0;
        _pinger.Hosts["10.0.0.3"] = () => 1.0;
        var e = Engine();
        var start = _clock.Now;
        _server.Streaming(Session("pp", null));
        await Ticks(e, 4);
        Assert.Equal([wifi.Id], e.Current.StreamingIds);
        Assert.Equal("10.0.0.2", Assert.Single(_history.QuerySessions(start.AddHours(-1), start.AddHours(1))).Address);
        Assert.DoesNotContain(wired.Id, e.Current.StreamingIds);
    }

    [Fact]
    public async Task Session_device_added_by_address_counts_as_streaming()
    {
        var e = Engine();
        _server.Streaming(Session("iPad", "192.168.1.40"));
        await Ticks(e, 1);
        Assert.Equal(e.Current.SessionDeviceIds, e.Current.StreamingIds);
    }

    [Fact]
    public async Task Nothing_is_marked_streaming_when_no_saved_name_matches()
    {
        Saved("Apple TV", "10.0.0.3");
        _pinger.Hosts["10.0.0.3"] = () => 1.0;
        var e = Engine();
        _server.Streaming(Session("pp", null));
        await Ticks(e, 3);
        Assert.Empty(e.Current.StreamingIds);
    }

    static ServerStatus Streaming(string name, long losses, long recoveries) =>
        new(ServerKind.Vibepollo, true,
            [new StreamSession(name, null, null, new StreamInfo(100_000, 120, losses, recoveries, 0, "HEVC"))],
            [name], null, new Dictionary<string, string>());

    [Fact]
    public async Task Losses_the_client_reports_behind_a_healthy_router_show_in_the_verdict()
    {
        _pinger.Hosts["192.168.1.1"] = () => 1;
        _server.Status = Streaming("pp", 0, 0);
        var e = Engine();
        await Ticks(e, 6);
        Assert.Equal("All clear", e.Current.Verdict.Message);

        _server.Status = Streaming("pp", 6, 0);
        await Ticks(e, 6);
        Assert.Equal(new Verdict(VerdictLevel.Warning, "Stream to pp has trouble past the router"), e.Current.Verdict);
    }
}
