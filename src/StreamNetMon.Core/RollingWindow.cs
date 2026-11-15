namespace StreamNetMon.Core;

/// <summary>Keeps recent probe results and turns them into latency, jitter and loss.</summary>
public sealed class RollingWindow(TimeSpan window, TimeSpan offlineAfter)
{
    private readonly Queue<ProbeResult> _samples = new();
    private readonly TimeSpan _keep = window > TimeSpan.FromSeconds(60) ? window : TimeSpan.FromSeconds(60);
    private DateTimeOffset? _lastReply;

    public void Add(ProbeResult r)
    {
        _samples.Enqueue(r);
        if (r.RttMs is not null) _lastReply = r.At;
        Trim(r.At);
    }

    public TargetStats Snapshot(DateTimeOffset now)
    {
        Trim(now);
        var inWindow = Recent(now, window);
        var offline = _lastReply is null || now - _lastReply.Value > offlineAfter;
        if (offline) return new TargetStats(null, null, 0, true, inWindow.Count, _lastReply is not null);

        double sum = 0, jitterSum = 0;
        int replies = 0, jitterPairs = 0;
        double? previous = null;
        foreach (var s in inWindow)
        {
            if (s.RttMs is not { } rtt) continue;
            sum += rtt;
            replies++;
            if (previous is { } p)
            {
                jitterSum += Math.Abs(rtt - p);
                jitterPairs++;
            }
            previous = rtt;
        }

        var loss = inWindow.Count == 0 ? 0 : (inWindow.Count - replies) * 100.0 / inWindow.Count;
        return new TargetStats(
            replies > 0 ? sum / replies : null,
            jitterPairs > 0 ? jitterSum / jitterPairs : null,
            loss, false, inWindow.Count, EverReplied: true);
    }

    /// <summary>Samples no older than <paramref name="span"/>, oldest first.</summary>
    public IReadOnlyList<ProbeResult> Recent(DateTimeOffset now, TimeSpan span)
    {
        var from = now - span;
        return _samples.Where(s => s.At > from).ToList();
    }

    private void Trim(DateTimeOffset now)
    {
        while (_samples.Count > 0 && now - _samples.Peek().At >= _keep) _samples.Dequeue();
    }
}
