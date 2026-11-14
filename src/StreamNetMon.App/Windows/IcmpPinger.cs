using System.Diagnostics;
using System.Net.NetworkInformation;

namespace StreamNetMon.App.Windows;

public sealed class IcmpPinger : IPinger
{
    private static readonly byte[] Payload = new byte[32];

    public Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct) => Task.Run<double?>(() =>
    {
        ct.ThrowIfCancellationRequested();
        using var ping = new Ping();

        // Windows reports round-trip time in whole milliseconds, far too coarse for a wired LAN
        // where replies take well under one. The blocking call returns the moment the reply
        // arrives, so timing it ourselves gives the missing precision.
        var started = Stopwatch.GetTimestamp();
        var reply = ping.Send(host, (int)timeout.TotalMilliseconds, Payload);
        var measured = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (reply.Status != IPStatus.Success) return null;
        // Our own figure can include a name lookup; never report more than Windows' figure plus its rounding.
        return Math.Min(measured, reply.RoundtripTime + 1.0);
    }, ct);
}
