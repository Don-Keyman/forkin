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

    /// <param name="troubledStreams">
    /// Streams whose client reports losses or keeps asking for frame recoveries (worst first). Used
    /// when nothing this PC can ping explains it, e.g. when the client sits behind a second router.
    /// </param>
    public static Verdict Evaluate(AdapterStats? adapter, IReadOnlyList<TargetState> targets, Thresholds th,
        IReadOnlyList<string>? troubledStreams = null)
    {
        // No adapter at all means the cable is out or the adapter is disabled.
        if (adapter is null || adapter.LinkSpeedBps < adapter.UsualLinkSpeedBps || adapter.OutErrorsDelta > 0)
            return new Verdict(VerdictLevel.Problem, "Desktop adapter problem");

        var degraded = targets.Where(t => IsDegraded(t, th)).ToList();
        if (degraded.Any(IsInfrastructure) || targets.Any(t => IsInfrastructure(t) && WentSilent(t)))
            return new Verdict(VerdictLevel.Problem, "Network path to router is degraded");

        var devices = degraded.Where(t => t.Device.Role == TargetRole.StreamingDevice).ToList();
        if (Worst(devices, ConnectionType.WiFi) is { } wifi)
            return new Verdict(VerdictLevel.Warning, $"Wi-Fi hop to {wifi.Device.Name} is degraded");
        if (Worst(devices, ConnectionType.Wired) is { } wired)
            return new Verdict(VerdictLevel.Warning, $"Wired path to {wired.Device.Name} is degraded");

        if (troubledStreams is [var stream, ..])
            return new Verdict(VerdictLevel.Warning, $"Stream to {stream} has trouble past the router");

        return Verdict.AllClear;
    }

    private static bool IsInfrastructure(TargetState t) => t.Device.Role is TargetRole.Router or TargetRole.Gateway;

    /// <summary>
    /// A router that used to answer and no longer does is a real problem. One that never
    /// answered may simply ignore ping, and a tablet going quiet is just asleep.
    /// </summary>
    public static bool WentSilent(TargetState t) => t.Stats.Offline && t.Stats.EverReplied;

    private static TargetState? Worst(IEnumerable<TargetState> devices, ConnectionType type) =>
        devices.Where(t => t.Device.Type == type).MaxBy(t => t.Stats.LossPct);
}
