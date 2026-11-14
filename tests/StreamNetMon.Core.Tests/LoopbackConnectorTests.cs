using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace StreamNetMon.Core.Tests;

public class LoopbackConnectorTests
{
    [Fact]
    public async Task Connects_to_an_ipv4_only_listener_without_waiting_on_ipv6()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var watch = Stopwatch.StartNew();
            await using var stream = await LoopbackConnector.ConnectAsync(new DnsEndPoint("localhost", port), default);
            Assert.True(watch.ElapsedMilliseconds < 1000, $"took {watch.ElapsedMilliseconds} ms");
            Assert.True(stream.CanWrite);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Falls_back_to_ipv6_loopback()
    {
        if (!Socket.OSSupportsIPv6) return;
        var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await using var stream = await LoopbackConnector.ConnectAsync(new DnsEndPoint("localhost", port), default);
            Assert.True(stream.CanWrite);
        }
        finally { listener.Stop(); }
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("192.168.1.10")]
    [InlineData("localhost.example.com")]
    public async Task Refuses_any_host_but_localhost(string host) =>
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await LoopbackConnector.ConnectAsync(new DnsEndPoint(host, 443), default));

    [Fact]
    public async Task Nothing_listening_is_an_HttpRequestException()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            await LoopbackConnector.ConnectAsync(new DnsEndPoint("localhost", port), default));
    }
}
