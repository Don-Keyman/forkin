namespace StreamNetMon.Core.Tests;

public class StreamServerLinkTests
{
    const string Sessions = "/api/rtsp/sessions", Status = "/api/session/status", Clients = "/api/clients/list", Stats = "/api/host/stats";

    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private readonly FakeClock _clock = new();
    private readonly FakeHttpHandler _http = new();
    private ServerMode _mode = ServerMode.Auto;
    private string? _token = "tok123";

    private StreamServerLink Link() => new(_http, _clock, () => _mode, () => 47990, () => _token)
    {
        RequestTimeout = TimeSpan.FromMilliseconds(100),
    };

    private FakeHttpHandler Vibepollo() => _http
        .Json(Status, "{}")
        .Json(Sessions, Fixture("vibepollo-sessions.json"))
        .Json(Clients, Fixture("clients-list.json"));

    [Fact]
    public async Task Detects_vibepollo_when_session_endpoint_exists()
    {
        Vibepollo();
        var s = await Link().PollAsync(default);
        Assert.Equal(ServerKind.Vibepollo, s.Kind);
        Assert.True(s.Running);
        Assert.Null(s.Note);
    }

    [Fact]
    public async Task Detects_vibepollo_even_when_session_endpoint_needs_login()
    {
        _http.Json(Status, "", 401);
        Assert.Equal(ServerKind.Vibepollo, (await Link().PollAsync(default)).Kind);
    }

    [Fact]
    public async Task Detects_sunshine_when_only_clients_list_exists()
    {
        _http.Json(Clients, Fixture("clients-list.json"));
        var s = await Link().PollAsync(default);
        Assert.Equal(ServerKind.Sunshine, s.Kind);
        Assert.True(s.Running);
        Assert.Equal(["iPad", "Shield"], s.PairedNames);
        Assert.Empty(s.Sessions);
    }

    [Fact]
    public async Task Detects_none_when_nothing_listens()
    {
        _http.Down = true;
        var s = await Link().PollAsync(default);
        Assert.Equal(ServerKind.None, s.Kind);
        Assert.False(s.Running);
    }

    [Fact]
    public async Task Mode_None_makes_no_requests()
    {
        Vibepollo();
        _mode = ServerMode.None;
        Assert.Equal(ServerStatus.Absent, await Link().PollAsync(default));
        Assert.Empty(_http.Requests);
    }

    [Fact]
    public async Task Forced_mode_skips_detection()
    {
        Vibepollo();
        _mode = ServerMode.Sunshine;
        var s = await Link().PollAsync(default);
        Assert.Equal(ServerKind.Sunshine, s.Kind);
        Assert.DoesNotContain(_http.Requests, r => r.RequestUri!.AbsolutePath == Status);
    }

    [Fact]
    public async Task Detection_is_cached_for_30s()
    {
        Vibepollo();
        var link = Link();
        await link.PollAsync(default);
        _clock.Advance(29);
        await link.PollAsync(default);
        Assert.Equal(1, _http.Requests.Count(r => r.RequestUri!.AbsolutePath == Status));
        _clock.Advance(2);
        await link.PollAsync(default);
        Assert.Equal(2, _http.Requests.Count(r => r.RequestUri!.AbsolutePath == Status));
    }

    [Fact]
    public async Task Sends_bearer_token_and_only_GETs_to_localhost()
    {
        Vibepollo();
        await Link().PollAsync(default);
        Assert.NotEmpty(_http.Requests);
        Assert.All(_http.Requests, r =>
        {
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.Equal("https", r.RequestUri!.Scheme);
            Assert.Equal("localhost", r.RequestUri.Host);
            Assert.Equal(47990, r.RequestUri.Port);
        });
        var sessions = _http.Requests.Single(r => r.RequestUri!.AbsolutePath == Sessions);
        Assert.Equal("Bearer", sessions.Headers.Authorization!.Scheme);
        Assert.Equal("tok123", sessions.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Parses_sessions_with_name_and_address()
    {
        Vibepollo();
        var s = await Link().PollAsync(default);
        Assert.Equal(2, s.Sessions.Count);
        Assert.Equal(new StreamSession("iPad", "192.168.1.40", DateTimeOffset.Parse("2026-11-14T11:30:00Z")), s.Sessions[0]);
        Assert.Equal(new StreamSession("Shield", "fe80::1", DateTimeOffset.FromUnixTimeSeconds(1794655800)), s.Sessions[1]);
        Assert.Equal(["iPad", "Shield"], s.PairedNames);
    }

    [Theory]
    [InlineData("192.168.1.40:51234", "192.168.1.40")]
    [InlineData("192.168.1.40", "192.168.1.40")]
    [InlineData("[fe80::1]:40000", "fe80::1")]
    [InlineData("fe80::1", "fe80::1")]
    [InlineData("::ffff:192.168.1.40", "192.168.1.40")]
    public void Address_port_suffix_is_stripped(string raw, string expected)
    {
        var json = "[{\"name\":\"x\",\"ip\":\"" + raw + "\"}]";
        Assert.Equal(expected, Assert.Single(VibepolloParser.Sessions(json)).Address);
    }

    [Fact]
    public void Session_without_known_fields_is_kept_with_nulls()
    {
        var s = Assert.Single(VibepolloParser.Sessions("[{\"device_name\":\"Phone\"}]"));
        Assert.Equal(new StreamSession("Phone", null, null), s);
    }

    [Fact]
    public async Task Falls_back_to_session_status_when_rtsp_sessions_is_missing()
    {
        _http.Json(Status, "{\"sessions\":[{\"name\":\"iPad\",\"ip\":\"10.0.0.9\"}]}").Json(Clients, "{}");
        var s = await Link().PollAsync(default);
        Assert.Equal("10.0.0.9", Assert.Single(s.Sessions).Address);
    }

    [Fact]
    public async Task Missing_token_gives_note_and_no_sessions()
    {
        Vibepollo();
        _token = null;
        var s = await Link().PollAsync(default);
        Assert.Equal(ServerKind.Vibepollo, s.Kind);
        Assert.Empty(s.Sessions);
        Assert.Equal("Add an access token in Settings to see live sessions", s.Note);
    }

    [Fact]
    public async Task Rejected_token_gives_note()
    {
        _http.Json(Status, "", 401).Json(Sessions, "{\"error\":\"unauthorized\"}", 401).Json(Clients, "", 401);
        var s = await Link().PollAsync(default);
        Assert.Equal(ServerKind.Vibepollo, s.Kind);
        Assert.Empty(s.Sessions);
        Assert.Equal("Access token was rejected", s.Note);
    }

    [Fact]
    public async Task Html_login_page_gives_unavailable_note()
    {
        _http.Json(Status, "{}");
        _http.Routes[Sessions] = (200, "text/html", "<!doctype html><html><body>Login</body></html>");
        var s = await Link().PollAsync(default);
        Assert.Equal(ServerKind.Vibepollo, s.Kind);
        Assert.Empty(s.Sessions);
        Assert.Equal("Server link unavailable", s.Note);
    }

    [Fact]
    public async Task Unexpected_json_shape_gives_empty_sessions()
    {
        _http.Json(Status, "{}").Json(Sessions, "{\"ok\":true}").Json(Clients, "[1,2,\"x\"]");
        var s = await Link().PollAsync(default);
        Assert.Empty(s.Sessions);
        Assert.Empty(s.PairedNames);
        Assert.Null(s.Note);
    }

    [Fact]
    public async Task Timeout_gives_unavailable_note_and_does_not_throw()
    {
        Vibepollo();
        _mode = ServerMode.Vibepollo;
        _http.Hang = true;
        var s = await Link().PollAsync(default);
        Assert.Equal("Server link unavailable", s.Note);
        Assert.Empty(s.Sessions);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        Vibepollo();
        _http.Hang = true;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Link().PollAsync(cts.Token));
    }

    [Fact]
    public async Task Host_stats_top_level_scalars_are_exposed_and_polled_every_5s()
    {
        Vibepollo().Json(Stats, "{\"cpu\":12.5,\"encoder\":\"nvenc\",\"busy\":true,\"nested\":{\"a\":1},\"list\":[1]}");
        var link = Link();
        var s = await link.PollAsync(default);
        Assert.Equal("12.5", s.HostStats["cpu"]);
        Assert.Equal("nvenc", s.HostStats["encoder"]);
        Assert.Equal("true", s.HostStats["busy"]);
        Assert.Equal(3, s.HostStats.Count);
        _clock.Advance(2);
        await link.PollAsync(default);
        Assert.Equal(1, _http.Requests.Count(r => r.RequestUri!.AbsolutePath == Stats));
        _clock.Advance(4);
        await link.PollAsync(default);
        Assert.Equal(2, _http.Requests.Count(r => r.RequestUri!.AbsolutePath == Stats));
    }

    [Fact]
    public async Task Server_going_away_is_noticed_at_next_detection()
    {
        Vibepollo();
        var link = Link();
        await link.PollAsync(default);
        _http.Down = true;
        _clock.Advance(31);
        Assert.Equal(ServerKind.None, (await link.PollAsync(default)).Kind);
    }

    [Fact]
    public async Task Keeps_last_response_bodies_for_diagnostics()
    {
        Vibepollo().Json(Stats, "{\"cpu\":1}");
        var link = Link();
        await link.PollAsync(default);
        Assert.Equal("{\"cpu\":1}", link.LastBodies[Stats]);
        Assert.Contains("sessions", link.LastBodies[Sessions]);
        Assert.All(link.LastBodies, kv => Assert.DoesNotContain("tok123", kv.Key + kv.Value));
    }

    [Fact]
    public async Task Token_limited_to_statistics_still_gives_host_stats_and_says_what_is_missing()
    {
        _http.Json(Status, "", 401).Json(Sessions, "{\"error\":\"Forbidden\"}", 403).Json(Clients, "", 403)
             .Json(Stats, "{\"cpu\":7}");
        var s = await Link().PollAsync(default);
        Assert.Equal(ServerKind.Vibepollo, s.Kind);
        Assert.Empty(s.Sessions);
        Assert.Equal("7", s.HostStats["cpu"]);
        Assert.Equal("This access token does not allow live sessions", s.Note);
    }

    // --- responses recorded from a real Vibepollo install (names and ids anonymised) ---

    [Fact]
    public async Task Real_vibepollo_session_has_name_stream_figures_and_a_start_derived_from_uptime()
    {
        _http.Json(Status, "{\"activeSessions\":1,\"status\":true}", 200)
             .Json(Sessions, Fixture("vibepollo-rtsp-sessions.real.json"))
             .Json(Clients, Fixture("vibepollo-clients.real.json"))
             .Json(Stats, Fixture("vibepollo-host-stats.real.json"));
        var s = await Link().PollAsync(default);
        var session = Assert.Single(s.Sessions);
        Assert.Equal("Tablet", session.ClientName);
        Assert.Null(session.Address);
        Assert.Equal(_clock.Now.AddSeconds(-65), session.StartedAt);
        Assert.Equal(["Tablet", "Phone"], s.PairedNames);

        var stream = session.Stream!;
        Assert.Equal(398_988, stream.BitrateKbps);
        Assert.Equal(120, stream.Fps);
        Assert.Equal(0, stream.ClientReportedLosses);
        Assert.Equal(0, stream.IdrRequests);
        Assert.Equal(2, stream.InvalidatedFrames);
        Assert.Equal("3840×2160 HEVC HDR", stream.Format);
    }

    [Fact]
    public void Session_without_stream_figures_has_no_stream_info() =>
        Assert.Null(Assert.Single(VibepolloParser.Sessions("[{\"name\":\"x\"}]", default)).Stream);
}
