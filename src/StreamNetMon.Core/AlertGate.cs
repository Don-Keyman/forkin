namespace StreamNetMon.Core;

/// <summary>
/// Decides when a problem deserves one notification: after it has lasted more than
/// ten seconds, and not again until things have been "All clear" in between.
/// </summary>
public sealed class AlertGate
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    private DateTimeOffset? _since;
    private bool _notified;

    public bool ShouldNotify(Verdict verdict, DateTimeOffset now)
    {
        if (verdict.Level == VerdictLevel.Ok)
        {
            _since = null;
            _notified = false;
            return false;
        }

        _since ??= now;
        if (_notified || now - _since.Value <= Delay) return false;
        _notified = true;
        return true;
    }
}
