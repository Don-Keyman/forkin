namespace StreamNetMon.Core.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
}

public sealed class FakePinger : IPinger
{
    public Dictionary<string, Func<double?>> Hosts { get; } = new();
    public List<string> Calls { get; } = new();

    public Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (Calls) Calls.Add(host);
        if (host == "nope.invalid") throw new System.Net.Sockets.SocketException(11001);
        return Task.FromResult(Hosts.TryGetValue(host, out var f) ? f() : null);
    }
}
