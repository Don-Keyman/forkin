using System.Net;
using System.Net.Sockets;

namespace StreamNetMon.Core;

/// <summary>
/// Opens connections to this PC only. "localhost" resolves to both an IPv6 and an IPv4 address,
/// and Windows takes two seconds to give up on whichever one the server is not listening on,
/// so both are tried at once and the first to connect wins.
/// </summary>
public static class LoopbackConnector
{
    public static async ValueTask<Stream> ConnectAsync(DnsEndPoint endPoint, CancellationToken ct)
    {
        if (!string.Equals(endPoint.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            throw new HttpRequestException($"Refusing to connect to '{endPoint.Host}': only this PC is allowed.");

        var addresses = Socket.OSSupportsIPv6
            ? new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }
            : new[] { IPAddress.Loopback };

        using var cancelLosers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var attempts = addresses.Select(a => TryConnectAsync(a, endPoint.Port, cancelLosers.Token)).ToList();
        Socket? winner = null;
        while (attempts.Count > 0 && winner is null)
        {
            var finished = await Task.WhenAny(attempts).ConfigureAwait(false);
            attempts.Remove(finished);
            winner = await finished.ConfigureAwait(false);
        }

        cancelLosers.Cancel();
        foreach (var loser in attempts)
            _ = loser.ContinueWith(t => t.Result?.Dispose(), TaskContinuationOptions.OnlyOnRanToCompletion);

        ct.ThrowIfCancellationRequested();
        if (winner is null) throw new HttpRequestException($"Nothing is listening on this PC's port {endPoint.Port}.");
        return new NetworkStream(winner, ownsSocket: true);
    }

    private static async Task<Socket?> TryConnectAsync(IPAddress address, int port, CancellationToken ct)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct).ConfigureAwait(false);
            return socket;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            socket.Dispose();
            return null;
        }
    }
}
