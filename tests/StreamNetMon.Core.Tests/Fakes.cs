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

/// <summary>Stands in for the streaming server's local web interface.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public Dictionary<string, (int Status, string ContentType, string Body)> Routes { get; } = new();
    public List<HttpRequestMessage> Requests { get; } = new();
    public bool Down { get; set; }
    public bool Hang { get; set; }

    public FakeHttpHandler Json(string path, string body, int status = 200)
    {
        Routes[path] = (status, "application/json", body);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        if (Down) throw new HttpRequestException("connection refused");
        if (Hang) await Task.Delay(Timeout.Infinite, ct);
        if (!Routes.TryGetValue(request.RequestUri!.AbsolutePath, out var r))
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        return new HttpResponseMessage((System.Net.HttpStatusCode)r.Status)
        {
            Content = new StringContent(r.Body, System.Text.Encoding.UTF8, r.ContentType),
        };
    }
}
