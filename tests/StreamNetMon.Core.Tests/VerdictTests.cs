namespace StreamNetMon.Core.Tests;

public class VerdictTests
{
    static readonly Thresholds Th = new();
    const long G25 = 2_500_000_000;

    static AdapterStats Adapter(long speed = G25, long errors = 0) => new("Ethernet", speed, G25, 0, 0, errors, 0);

    static TargetState T(string name, ConnectionType type, TargetRole role = TargetRole.StreamingDevice,
        double latency = 1, double jitter = 0.2, double loss = 0, bool offline = false, int samples = 30) =>
        new(new Device(Guid.NewGuid(), name, "10.0.0.2", type, role),
            offline ? new TargetStats(null, null, 0, true, samples) : new TargetStats(latency, jitter, loss, false, samples),
            []);

    static Verdict Eval(AdapterStats? a, params TargetState[] t) => VerdictEngine.Evaluate(a, t, Th);

    [Fact]
    public void All_clear_when_everything_is_healthy()
    {
        var v = Eval(Adapter(), T("Router", ConnectionType.Wired, TargetRole.Router), T("iPad", ConnectionType.WiFi));
        Assert.Equal(new Verdict(VerdictLevel.Ok, "All clear"), v);
    }

    [Fact]
    public void All_clear_with_no_adapter_and_no_targets() =>
        Assert.Equal("All clear", Eval(null).Message);

    [Fact]
    public void Link_speed_drop_is_adapter_problem() =>
        Assert.Equal(new Verdict(VerdictLevel.Problem, "Desktop adapter problem"), Eval(Adapter(speed: 100_000_000)));

    [Fact]
    public void Send_errors_are_adapter_problem() =>
        Assert.Equal("Desktop adapter problem", Eval(Adapter(errors: 1)).Message);

    [Fact]
    public void Degraded_router_is_network_path_problem()
    {
        var v = Eval(Adapter(), T("Router", ConnectionType.Wired, TargetRole.Router, loss: 3));
        Assert.Equal(new Verdict(VerdictLevel.Problem, "Network path to router is degraded"), v);
    }

    [Fact]
    public void Degraded_gateway_is_network_path_problem() =>
        Assert.Equal("Network path to router is degraded",
            Eval(Adapter(), T("Gateway", ConnectionType.Wired, TargetRole.Gateway, latency: 12)).Message);

    [Fact]
    public void Degraded_wifi_device_names_the_wifi_hop()
    {
        var v = Eval(Adapter(), T("Router", ConnectionType.Wired, TargetRole.Router), T("iPad Wi-Fi", ConnectionType.WiFi, loss: 2.1));
        Assert.Equal(new Verdict(VerdictLevel.Warning, "Wi-Fi hop to iPad Wi-Fi is degraded"), v);
    }

    [Fact]
    public void Degraded_wired_device_names_the_wired_path()
    {
        var v = Eval(Adapter(), T("Shield", ConnectionType.Wired, jitter: 6));
        Assert.Equal(new Verdict(VerdictLevel.Warning, "Wired path to Shield is degraded"), v);
    }

    [Fact]
    public void Router_problem_outranks_device_problem() =>
        Assert.Equal("Network path to router is degraded",
            Eval(Adapter(), T("iPad", ConnectionType.WiFi, loss: 9), T("Router", ConnectionType.Wired, TargetRole.Router, loss: 3)).Message);

    [Fact]
    public void Adapter_problem_outranks_router_problem() =>
        Assert.Equal("Desktop adapter problem",
            Eval(Adapter(errors: 2), T("Router", ConnectionType.Wired, TargetRole.Router, loss: 3)).Message);

    [Fact]
    public void Wifi_device_outranks_wired_device() =>
        Assert.Equal("Wi-Fi hop to iPad is degraded",
            Eval(Adapter(), T("Shield", ConnectionType.Wired, loss: 9), T("iPad", ConnectionType.WiFi, loss: 2)).Message);

    [Fact]
    public void Worst_loss_is_named_among_several() =>
        Assert.Equal("Wi-Fi hop to Phone is degraded",
            Eval(Adapter(), T("iPad", ConnectionType.WiFi, loss: 2), T("Phone", ConnectionType.WiFi, loss: 5)).Message);

    [Fact]
    public void Offline_device_is_not_degraded() =>
        Assert.Equal("All clear", Eval(Adapter(), T("iPad", ConnectionType.WiFi, offline: true)).Message);

    [Fact]
    public void Thresholds_are_inclusive()
    {
        Assert.True(VerdictEngine.IsDegraded(T("a", ConnectionType.Wired, loss: 1.0), Th));
        Assert.False(VerdictEngine.IsDegraded(T("a", ConnectionType.Wired, loss: 0.9), Th));
        Assert.True(VerdictEngine.IsDegraded(T("a", ConnectionType.WiFi, latency: 30), Th));
    }

    [Fact]
    public void Wifi_thresholds_apply_to_wifi_only()
    {
        Assert.True(VerdictEngine.IsDegraded(T("a", ConnectionType.Wired, jitter: 10), Th));
        Assert.False(VerdictEngine.IsDegraded(T("a", ConnectionType.WiFi, jitter: 10), Th));
        Assert.True(VerdictEngine.IsDegraded(T("a", ConnectionType.Wired, latency: 20), Th));
        Assert.False(VerdictEngine.IsDegraded(T("a", ConnectionType.WiFi, latency: 20), Th));
    }

    [Fact]
    public void Fewer_than_5_samples_is_not_degraded() =>
        Assert.False(VerdictEngine.IsDegraded(T("a", ConnectionType.Wired, loss: 50, samples: 4), Th));
}
