namespace StreamNetMon.Core;

/// <summary>Probes one device and keeps its rolling statistics.</summary>
public sealed class TargetMonitor(Device device, IPinger pinger, IClock clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly RollingWindow _window = new(Window, OfflineAfter);
    private readonly object _gate = new();

    public Device Device { get; } = device;

    public async Task ProbeOnceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var at = clock.Now;
        double? rtt;
        try
        {
            rtt = await pinger.PingAsync(Device.Host, Timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Unresolvable host, unreachable network, etc. — the same to the user as no reply.
            rtt = null;
        }

        lock (_gate) _window.Add(new ProbeResult(at, rtt));
    }

    public TargetState State()
    {
        var now = clock.Now;
        lock (_gate)
            return new TargetState(Device, _window.Snapshot(now), _window.Recent(now, TimeSpan.FromSeconds(60)));
    }
}
