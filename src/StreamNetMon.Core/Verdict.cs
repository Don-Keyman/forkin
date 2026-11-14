namespace StreamNetMon.Core;

/// <summary>Values at or above which a target counts as degraded.</summary>
public sealed record Thresholds(double LossPct = 1, double JitterWiredMs = 5, double JitterWiFiMs = 15,
                                double LatencyWiredMs = 10, double LatencyWiFiMs = 30);

public enum VerdictLevel { Ok, Warning, Problem }

public sealed record Verdict(VerdictLevel Level, string Message)
{
    public static readonly Verdict AllClear = new(VerdictLevel.Ok, "All clear");
}

public static class VerdictEngine
{
    /// <summary>Below this many samples a target has too little data to judge.</summary>
    public const int MinSamples = 5;

    public static bool IsDegraded(TargetState t, Thresholds th)
    {
        var s = t.Stats;
        if (s.Offline || s.Samples < MinSamples) return false;
        var wifi = t.Device.Type == ConnectionType.WiFi;
        return s.LossPct >= th.LossPct
               || s.JitterMs >= (wifi ? th.JitterWiFiMs : th.JitterWiredMs)
               || s.LatencyMs >= (wifi ? th.LatencyWiFiMs : th.LatencyWiredMs);
    }

    public static Verdict Evaluate(AdapterStats? adapter, IReadOnlyList<TargetState> targets, Thresholds th)
    {
        if (adapter is not null && (adapter.LinkSpeedBps < adapter.UsualLinkSpeedBps || adapter.OutErrorsDelta > 0))
            return new Verdict(VerdictLevel.Problem, "Desktop adapter problem");

        var degraded = targets.Where(t => IsDegraded(t, th)).ToList();
        if (degraded.Any(t => t.Device.Role is TargetRole.Router or TargetRole.Gateway))
            return new Verdict(VerdictLevel.Problem, "Network path to router is degraded");

        var devices = degraded.Where(t => t.Device.Role == TargetRole.StreamingDevice).ToList();
        if (Worst(devices, ConnectionType.WiFi) is { } wifi)
            return new Verdict(VerdictLevel.Warning, $"Wi-Fi hop to {wifi.Device.Name} is degraded");
        if (Worst(devices, ConnectionType.Wired) is { } wired)
            return new Verdict(VerdictLevel.Warning, $"Wired path to {wired.Device.Name} is degraded");

        return Verdict.AllClear;
    }

    private static TargetState? Worst(IEnumerable<TargetState> devices, ConnectionType type) =>
        devices.Where(t => t.Device.Type == type).MaxBy(t => t.Stats.LossPct);
}
