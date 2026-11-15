using System.Net;
using System.Net.Http.Headers;

namespace StreamNetMon.Core;

public enum ServerKind { None, Sunshine, Vibepollo }

/// <summary>What the server says about one running stream. Counters are totals since the stream began.</summary>
public sealed record StreamInfo(double BitrateKbps, double Fps, long ClientReportedLosses, long IdrRequests,
    long InvalidatedFrames, string Format);

public sealed record StreamSession(string ClientName, string? Address, DateTimeOffset? StartedAt, StreamInfo? Stream = null);

public sealed record ServerStatus(ServerKind Kind, bool Running, IReadOnlyList<StreamSession> Sessions,
    IReadOnlyList<string> PairedNames, string? Note, IReadOnlyDictionary<string, string> HostStats)
{
    public static ServerStatus Absent { get; } = new(ServerKind.None, false, [], [], null, new Dictionary<string, string>());
}

public interface IStreamServer
{
    /// <summary>The server's current state. Never throws, except when cancelled.</summary>
    Task<ServerStatus> PollAsync(CancellationToken ct);
}

/// <summary>
/// Read-only link to a Sunshine-family streaming server's local web interface.
/// Only ever sends GET requests to https://localhost.
/// </summary>
public sealed class StreamServerLink : IStreamServer
{
    public const string NoTokenNote = "Add an access token in Settings to see live sessions";
    public const string RejectedNote = "Access token was rejected";
    public const string UnavailableNote = "Server link unavailable";
    public const string LimitedNote = "This access token does not allow live sessions";

    private const string SessionsPath = "/api/rtsp/sessions";
    private const string StatusPath = "/api/session/status";
    private const string ClientsPath = "/api/clients/list";
    private const string StatsPath = "/api/host/stats";

    private static readonly TimeSpan DetectEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StatsEvery = TimeSpan.FromSeconds(5);
    private static readonly IReadOnlyDictionary<string, string> NoStats = new Dictionary<string, string>();

    private readonly HttpClient _http;
    private readonly IClock _clock;
    private readonly Func<ServerMode> _mode;
    private readonly Func<int> _port;
    private readonly Func<string?> _token;

    private ServerKind _detected;
    private DateTimeOffset? _detectedAt;
    private IReadOnlyDictionary<string, string> _stats = NoStats;
    private DateTimeOffset? _statsAt;
    private bool _statsAllowed;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _lastBodies = new();

    public StreamServerLink(HttpMessageHandler handler, IClock clock, Func<ServerMode> mode, Func<int> port, Func<string?> token)
    {
        _http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        _clock = clock;
        _mode = mode;
        _port = port;
        _token = token;
    }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The most recent response body per endpoint, for the diagnostics export. Never holds the token.</summary>
    public IReadOnlyDictionary<string, string> LastBodies => _lastBodies;

    public async Task<ServerStatus> PollAsync(CancellationToken ct)
    {
        try
        {
            var kind = _mode() switch
            {
                ServerMode.None => ServerKind.None,
                ServerMode.Vibepollo => ServerKind.Vibepollo,
                ServerMode.Sunshine => ServerKind.Sunshine,
                _ => await DetectAsync(ct).ConfigureAwait(false),
            };
            return kind switch
            {
                ServerKind.Vibepollo => await PollVibepolloAsync(ct).ConfigureAwait(false),
                ServerKind.Sunshine => await PollSunshineAsync(ct).ConfigureAwait(false),
                _ => ServerStatus.Absent,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Whatever the server did, monitoring must carry on.
            return ServerStatus.Absent with { Note = UnavailableNote };
        }
    }

    private async Task<ServerKind> DetectAsync(CancellationToken ct)
    {
        var now = _clock.Now;
        if (_detectedAt is { } at && now - at < DetectEvery) return _detected;

        var status = await GetAsync(StatusPath, ct).ConfigureAwait(false);
        if (status.Outcome is Outcome.Ok or Outcome.Unauthorized or Outcome.OtherStatus)
            _detected = ServerKind.Vibepollo;
        else if (status.Outcome == Outcome.NotFound && (await GetAsync(ClientsPath, ct).ConfigureAwait(false)).Outcome != Outcome.Failed)
            _detected = ServerKind.Sunshine;
        else
            _detected = ServerKind.None;

        _detectedAt = now;
        return _detected;
    }

    private async Task<ServerStatus> PollVibepolloAsync(CancellationToken ct)
    {
        var status = new ServerStatus(ServerKind.Vibepollo, true, [], [], null, NoStats);
        if (string.IsNullOrEmpty(_token())) return status with { Note = NoTokenNote };

        var sessions = await GetAsync(SessionsPath, ct).ConfigureAwait(false);
        if (sessions.Outcome == Outcome.NotFound) sessions = await GetAsync(StatusPath, ct).ConfigureAwait(false);
        var sessionsRefused = false;
        switch (sessions.Outcome)
        {
            case Outcome.Unauthorized:
                // The token may simply not cover sessions; see what else it allows before judging it.
                sessionsRefused = true;
                break;
            case Outcome.Ok when JsonWalk.TryParse(sessions.Body) is { } doc:
                doc.Dispose();
                status = status with { Sessions = VibepolloParser.Sessions(sessions.Body, _clock.Now) };
                break;
            default:
                return status with { Note = UnavailableNote };
        }

        var clients = await GetAsync(ClientsPath, ct).ConfigureAwait(false);
        if (clients.Outcome == Outcome.Ok) status = status with { PairedNames = VibepolloParser.PairedNames(clients.Body) };

        var now = _clock.Now;
        if (_statsAt is not { } at || now - at >= StatsEvery)
        {
            var stats = await GetAsync(StatsPath, ct).ConfigureAwait(false);
            _stats = stats.Outcome == Outcome.Ok ? VibepolloParser.HostStats(stats.Body) : NoStats;
            _statsAllowed = stats.Outcome == Outcome.Ok;
            _statsAt = now;
        }

        if (sessionsRefused)
            status = status with { Note = _statsAllowed || clients.Outcome == Outcome.Ok ? LimitedNote : RejectedNote };
        return status with { HostStats = _stats };
    }

    private async Task<ServerStatus> PollSunshineAsync(CancellationToken ct)
    {
        var clients = await GetAsync(ClientsPath, ct).ConfigureAwait(false);
        if (clients.Outcome == Outcome.Failed) return ServerStatus.Absent with { Kind = ServerKind.Sunshine, Note = UnavailableNote };
        var names = clients.Outcome == Outcome.Ok ? SunshineParser.PairedNames(clients.Body) : [];
        return new ServerStatus(ServerKind.Sunshine, true, [], names, null, NoStats);
    }

    private enum Outcome { Ok, NotFound, Unauthorized, OtherStatus, Failed }

    private async Task<(Outcome Outcome, string Body)> GetAsync(string path, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://localhost:{_port()}{path}"));
            if (_token() is { Length: > 0 } token) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            _lastBodies[path] = body;
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => (Outcome.NotFound, body),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (Outcome.Unauthorized, body),
                _ when response.IsSuccessStatusCode => (Outcome.Ok, body),
                _ => (Outcome.OtherStatus, body),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (Outcome.Failed, "");   // our own timeout
        }
        catch (HttpRequestException)
        {
            return (Outcome.Failed, "");
        }
    }
}
