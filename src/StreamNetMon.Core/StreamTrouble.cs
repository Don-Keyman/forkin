namespace StreamNetMon.Core;

/// <summary>
/// Turns the server's running totals for each stream into "is this stream in trouble right now".
/// The client reports these figures itself, so they cover the whole path to it, including any
/// router that hides the client from this PC.
/// </summary>
public sealed class StreamTroubleTracker
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>New client-reported losses within the window at or above which a stream is in trouble.</summary>
    public const long LossesInWindow = 2;

    /// <summary>New frame recoveries within the window at or above which a stream is in trouble.</summary>
    public const long RecoveriesInWindow = 3;

    private readonly record struct Sample(DateTimeOffset At, long Losses, long Recoveries);

    private readonly Dictionary<string, List<Sample>> _streams = new(StringComparer.OrdinalIgnoreCase);

    public void Observe(IReadOnlyList<StreamSession> sessions, DateTimeOffset now)
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sessions)
        {
            if (s.Stream is not { } info) continue;
            live.Add(s.ClientName);
            if (!_streams.TryGetValue(s.ClientName, out var samples))
                _streams[s.ClientName] = samples = new List<Sample>();

            // Totals only grow during a stream; a drop means the client reconnected.
            if (samples.Count > 0 && (info.ClientReportedLosses < samples[^1].Losses || info.IdrRequests < samples[^1].Recoveries))
                samples.Clear();

            samples.Add(new Sample(now, info.ClientReportedLosses, info.IdrRequests));
            // Keep the newest sample at or before the window's start as the baseline.
            while (samples.Count > 1 && samples[1].At <= now - Window) samples.RemoveAt(0);
        }

        foreach (var gone in _streams.Keys.Where(k => !live.Contains(k)).ToList()) _streams.Remove(gone);
    }

    /// <summary>Names of the streams with trouble in the last <see cref="Window"/>, worst first.</summary>
    public IReadOnlyList<string> Troubled() =>
        _streams
            .Select(kv => (Name: kv.Key, First: kv.Value[0], Last: kv.Value[^1]))
            .Select(x => (x.Name, Losses: x.Last.Losses - x.First.Losses, Recoveries: x.Last.Recoveries - x.First.Recoveries))
            .Where(x => x.Losses >= LossesInWindow || x.Recoveries >= RecoveriesInWindow)
            .OrderByDescending(x => x.Losses).ThenByDescending(x => x.Recoveries)
            .Select(x => x.Name)
            .ToList();
}
