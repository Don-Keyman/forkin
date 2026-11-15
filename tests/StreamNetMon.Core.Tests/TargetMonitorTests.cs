namespace StreamNetMon.Core.Tests;

public class TargetMonitorTests
{
    static Device Dev(string host) => new(Guid.NewGuid(), "iPad", host, ConnectionType.WiFi, TargetRole.StreamingDevice);

    [Fact]
    public async Task Reply_is_recorded()
    {
        var clock = new FakeClock();
        var pinger = new FakePinger { Hosts = { ["10.0.0.5"] = () => 4.0 } };
        var m = new TargetMonitor(Dev("10.0.0.5"), pinger, clock);
        await m.ProbeOnceAsync(default);
        var s = m.State();
        Assert.Equal(4.0, s.Stats.LatencyMs);
        Assert.False(s.Stats.Offline);
        Assert.Single(s.Last60s);
    }

    [Fact]
    public async Task Timeout_is_recorded_as_loss()
    {
        var clock = new FakeClock();
        double? next = 4.0;
        var pinger = new FakePinger { Hosts = { ["10.0.0.5"] = () => next } };
        var m = new TargetMonitor(Dev("10.0.0.5"), pinger, clock);
        await m.ProbeOnceAsync(default);
        clock.Advance(1);
        next = null;
        await m.ProbeOnceAsync(default);
        Assert.Equal(50, m.State().Stats.LossPct);
    }

    [Fact]
    public async Task Pinger_exception_is_recorded_as_loss_and_not_thrown()
    {
        var m = new TargetMonitor(Dev("nope.invalid"), new FakePinger(), new FakeClock());
        await m.ProbeOnceAsync(default);
        var s = m.State();
        Assert.True(s.Stats.Offline);
        Assert.Single(s.Last60s);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var m = new TargetMonitor(Dev("10.0.0.5"), new FakePinger(), new FakeClock());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => m.ProbeOnceAsync(cts.Token));
    }
}
