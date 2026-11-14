using System.Net.NetworkInformation;

namespace StreamNetMon.App.Windows;

public sealed class IcmpPinger : IPinger
{
    private static readonly byte[] Payload = new byte[32];

    public async Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        using var ping = new Ping();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var reply = await ping.SendPingAsync(host, timeout, Payload, null, ct).ConfigureAwait(false);
        if (reply.Status != IPStatus.Success) return null;

        // RoundtripTime is whole milliseconds, far too coarse for a wired LAN where replies
        // take well under one. Use our own clock when it reads 0 or 1.
        var measured = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return reply.RoundtripTime <= 1 ? Math.Min(measured, 1.5) : reply.RoundtripTime;
    }
}
