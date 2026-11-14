# Stream Network Monitor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Windows tray app that measures connection quality from the streaming host to in-home client devices and says where a bad stream is coming from.

**Architecture:** A UI-free core library holds all logic (rolling statistics, verdict, storage, streaming-server link) behind small interfaces and is tested with fakes. A WPF app supplies the Windows-specific sources (ping, adapter counters, neighbour table, DPAPI) and the window, tray icon and toasts. One engine object ticks once a second and publishes a snapshot the UI binds to.

**Tech Stack:** .NET 10, C#, WPF, xUnit, Microsoft.Data.Sqlite, System.Text.Json, ScottPlot.WPF (charts), H.NotifyIcon.Wpf (tray).

**Spec:** `docs/superpowers/specs/2026-11-14-stream-network-monitor-design.md`

## Global Constraints

- Target frameworks: `net10.0` (Core, tests), `net10.0-windows` (App). Nullable enabled, warnings as errors in Core.
- No administrator rights, no listening ports, no GPU-accelerated animation.
- Never read `C:\Program Files\Apollo\config\credentials`, `*_state.json`, or `session_history`.
- The streaming-server link makes GET requests only, to `https://localhost:<port>` only (default port `47990`), and accepts a self-signed certificate for `localhost` only.
- The access token is stored only as a DPAPI-protected string, never logged, never shown in full after entry.
- Probe: one ICMP echo per target per second, 1 s timeout. Rolling window 30 s. Offline after 60 s without a reply.
- Degraded defaults: loss ≥ 1%; jitter ≥ 5 ms wired / ≥ 15 ms Wi-Fi; latency ≥ 10 ms wired / ≥ 30 ms Wi-Fi.
- Storage: `%LOCALAPPDATA%\StreamNetMon\` — `history.db` (SQLite, 5 s buckets, 14-day retention), `settings.json`.
- Server polling: detection every 30 s, sessions every 2 s, host statistics every 5 s.
- Footprint: under 1% average CPU and under 150 MB memory with the window hidden; charts redraw at most once per second and not while hidden.
- Verdict messages, exactly: `All clear`, `Desktop adapter problem`, `Network path to router is degraded`, `Wi-Fi hop to {name} is degraded`, `Wired path to {name} is degraded`.
- Build environments: the Linux container has the .NET 10 SDK at `/root/.dotnet/dotnet` — Core and tests build and run there; the App compiles there with `-p:EnableWindowsTargeting=true` but only runs on Stella. On Stella (Desktop Commander) commands return after 3 s: start builds in the background, redirect output to a file, read the file.

## Review Focus

1. A device host that does not resolve or is not a valid address → that row shows offline; other targets keep being probed. (Task 3)
2. The PC sleeps and resumes, or a tick is late → no throughput spike and no false "degraded" from the gap. (Tasks 2, 4)
3. Adapter counters go backwards (driver reset, adapter re-enabled) or the adapter disappears → rates read as zero/unknown, never negative or huge. (Task 4)
4. `settings.json` is corrupt, empty or from an older version → app starts with defaults and keeps the bad file as `settings.json.bad`. (Task 6)
5. The server answers with a login page, 401, HTML, or JSON of an unexpected shape (wrong/expired token, fork update) → link reports a note, kind stays detected, no exception reaches the engine. (Task 8)

---

## File Structure

```
StreamNetMon.sln
Directory.Build.props                     nullable, langversion, warnings
src/StreamNetMon.Core/
  Model.cs            Device, ConnectionType, TargetRole, ProbeResult, TargetStats, TargetState
  Abstractions.cs     IClock, IPinger, IAdapterSource, ISecretProtector, INeighbourSource
  RollingWindow.cs    30 s window → TargetStats
  TargetMonitor.cs    one target: probe + window
  AdapterTracker.cs   counter readings → AdapterStats
  Verdict.cs          Thresholds, Verdict, VerdictEngine
  Settings.cs         AppSettings, SettingsStore
  History.cs          BucketAggregator, HistoryStore
  StreamServer.cs     ServerMode, ServerKind, StreamSession, ServerStatus, IStreamServer, StreamServerLink
  ServerParsers.cs    VibepolloParser, SunshineParser
  MonitorEngine.cs    1 s tick, session devices, Snapshot
src/StreamNetMon.App/
  App.xaml(.cs)               startup, single instance, tray
  Windows/                    IcmpPinger, WindowsAdapterSource, NeighbourTable, DpapiProtector, StartupRegistration
  MainWindow.xaml(.cs)        shell + tabs
  Views/                      LiveView, HistoryView, DevicesView, SettingsView
  ViewModels/                 MainViewModel and one per view
tests/StreamNetMon.Core.Tests/            one test file per Core file, Fakes.cs, Fixtures/*.json
```

---

### Task 1: Solution skeleton, Node scaffold removed

**Files:**
- Delete: `package.json`, `package-lock.json`, `tsconfig.json`, `tsconfig.build.json`, `.nvmrc`, `src/index.ts`, `test/index.test.ts`
- Create: `StreamNetMon.sln`, `Directory.Build.props`, the three `.csproj` files, `src/StreamNetMon.App/App.xaml(.cs)`, `MainWindow.xaml(.cs)` (empty window titled "Stream network monitor")
- Modify: `.gitignore` (replace with .NET ignores: `bin/`, `obj/`, `*.user`, `.vs/`, `publish/`), `README.md` (what it is, build/test/run commands)

**Interfaces:**
- Produces: project names and namespaces `StreamNetMon.Core`, `StreamNetMon.App`, `StreamNetMon.Core.Tests`. App references Core; tests reference Core. App csproj has `<UseWPF>true</UseWPF>`, `<OutputType>WinExe</OutputType>`.

- [ ] **Step 1:** Remove the Node files; create solution and projects with `dotnet new sln/classlib/xunit/wpf` and add references.
- [ ] **Step 2:** Add one placeholder test `SmokeTests.Core_assembly_loads` asserting `typeof(StreamNetMon.Core.Device).Assembly != null` (with an empty `Device` record in `Model.cs`).
- [ ] **Step 3:** Run `dotnet test` — Expected: 1 passed. Run `dotnet build src/StreamNetMon.App -p:EnableWindowsTargeting=true` — Expected: Build succeeded, 0 warnings.
- [ ] **Step 4:** Commit `chore: replace Node scaffold with .NET solution`.

### Task 2: Rolling statistics

**Files:** Create `src/StreamNetMon.Core/Model.cs`, `Abstractions.cs` (IClock only), `RollingWindow.cs`; Test `tests/.../RollingWindowTests.cs`, `Fakes.cs` (`FakeClock`).

**Interfaces — Produces:**
```csharp
public enum ConnectionType { Wired, WiFi }
public enum TargetRole { StreamingDevice, Router, Gateway }
public sealed record Device(Guid Id, string Name, string Host, ConnectionType Type, TargetRole Role);
public readonly record struct ProbeResult(DateTimeOffset At, double? RttMs);      // null = no reply
public sealed record TargetStats(double? LatencyMs, double? JitterMs, double LossPct, bool Offline, int Samples);
public interface IClock { DateTimeOffset Now { get; } }
public sealed class RollingWindow(TimeSpan window, TimeSpan offlineAfter) {
    public void Add(ProbeResult r);
    public TargetStats Snapshot(DateTimeOffset now);
    public IReadOnlyList<ProbeResult> Recent(DateTimeOffset now, TimeSpan span);   // for sparklines
}
```
Rules: latency = mean RTT of replies in window; jitter = mean |RTTᵢ − RTTᵢ₋₁| over consecutive replies (null with fewer than 2); loss = lost / total × 100; offline = no reply within `offlineAfter` of `now` (also true when no reply ever). When offline, `LossPct` is 0 and latency/jitter are null. Results older than `window` are dropped on `Add` and `Snapshot`.

- [ ] **Step 1: Failing tests**
```csharp
Latency_is_mean_of_replies()            // 10,20,30 ms → 20
Jitter_is_mean_abs_consecutive_diff()   // 10,14,11 → 3.5
Loss_counts_missing_replies()           // 3 replies + 1 null → 25.0
Old_samples_leave_the_window()          // sample at t0, snapshot at t0+31s → Samples == 0
Offline_after_60s_without_reply()       // reply at t0, nulls to t0+61s → Offline, LossPct == 0, LatencyMs == null
Never_replied_is_offline()
Gap_in_samples_does_not_count_as_loss() // reply t0, reply t0+20s, nothing between → LossPct == 0   (Review Focus 2)
```
- [ ] **Step 2:** `dotnet test --filter RollingWindowTests` — Expected: FAIL (type missing).
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Tests pass.
- [ ] **Step 5:** Commit `feat: rolling latency/jitter/loss window`.

### Task 3: Target probing

**Files:** Modify `Abstractions.cs`; Create `TargetMonitor.cs`; Test `TargetMonitorTests.cs`; add `FakePinger` to `Fakes.cs`.

**Interfaces:**
- Consumes: `RollingWindow`, `Device`, `ProbeResult`, `IClock`.
- Produces:
```csharp
public interface IPinger { Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct); } // null = no reply; may throw
public sealed record TargetState(Device Device, TargetStats Stats, IReadOnlyList<ProbeResult> Last60s);
public sealed class TargetMonitor(Device device, IPinger pinger, IClock clock) {
    public Device Device { get; }
    public Task ProbeOnceAsync(CancellationToken ct);   // 1 s timeout; any exception from the pinger is recorded as a lost probe
    public TargetState State();                         // window 30 s, offline 60 s
}
```

- [ ] **Step 1: Failing tests** — `Reply_is_recorded`, `Timeout_is_recorded_as_loss`, `Pinger_exception_is_recorded_as_loss_and_not_thrown` (Review Focus 1: `FakePinger` throws `SocketException` for host `"nope.invalid"`), `Cancellation_propagates`.
- [ ] **Step 2–4:** Fail, implement, pass.
- [ ] **Step 5:** Commit `feat: per-target probing`.

### Task 4: Adapter tracking

**Files:** Modify `Abstractions.cs`; Create `AdapterTracker.cs`; Test `AdapterTrackerTests.cs`.

**Interfaces — Produces:**
```csharp
public sealed record AdapterReading(string Name, long LinkSpeedBps, long BytesSent, long BytesReceived, long OutErrors, long OutDiscards);
public interface IAdapterSource { AdapterReading? Read(); }     // null = no active adapter
public sealed record AdapterStats(string Name, long LinkSpeedBps, long UsualLinkSpeedBps,
    double SendBps, double ReceiveBps, long OutErrorsDelta, long OutDiscardsDelta);
public sealed class AdapterTracker {
    public AdapterStats? Update(AdapterReading? reading, DateTimeOffset now);
}
```
Rules: rates = counter delta × 8 / elapsed seconds. First reading, a different adapter name, a negative delta, or elapsed > 5 s → rates and deltas are 0 for that update and the baseline resets. `UsualLinkSpeedBps` = highest link speed seen for that adapter name. `null` reading → `null`.

- [ ] **Step 1: Failing tests** — `Rate_from_two_readings` (1,250,000 bytes in 1 s → 10,000,000 bps), `First_reading_has_zero_rate`, `Counter_going_backwards_gives_zero_not_negative` (RF 3), `Long_gap_gives_zero_rate` (RF 2: 30 s between readings), `Adapter_change_resets_baseline`, `Null_reading_gives_null`, `Usual_speed_remembers_highest` (2.5 G then 100 M → usual stays 2.5 G), `Error_and_discard_deltas`.
- [ ] **Step 2–4:** Fail, implement, pass.
- [ ] **Step 5:** Commit `feat: adapter throughput and error tracking`.

### Task 5: Verdict

**Files:** Create `Verdict.cs`; Test `VerdictTests.cs`.

**Interfaces:**
- Consumes: `TargetState`, `AdapterStats`.
- Produces:
```csharp
public sealed record Thresholds(double LossPct = 1, double JitterWiredMs = 5, double JitterWiFiMs = 15,
                                double LatencyWiredMs = 10, double LatencyWiFiMs = 30);
public enum VerdictLevel { Ok, Warning, Problem }
public sealed record Verdict(VerdictLevel Level, string Message);
public static class VerdictEngine {
    public static bool IsDegraded(TargetState t, Thresholds th);   // false when offline or Samples < 5
    public static Verdict Evaluate(AdapterStats? adapter, IReadOnlyList<TargetState> targets, Thresholds th);
}
```
Order (first match wins): adapter `LinkSpeedBps < UsualLinkSpeedBps` or `OutErrorsDelta > 0` → Problem `Desktop adapter problem`; any Router/Gateway degraded → Problem `Network path to router is degraded`; a Wi-Fi StreamingDevice degraded → Warning `Wi-Fi hop to {name} is degraded`; a Wired StreamingDevice degraded → Warning `Wired path to {name} is degraded`; else Ok `All clear`. With several degraded devices of the same kind, name the one with the highest loss.

- [ ] **Step 1: Failing tests** — one per rule and message above, plus `Offline_device_is_not_degraded`, `Thresholds_are_inclusive` (loss exactly 1.0 → degraded), `Wifi_thresholds_apply_to_wifi_only` (jitter 10 ms: wired degraded, Wi-Fi not), `Fewer_than_5_samples_is_not_degraded`, `Router_problem_outranks_device_problem`.
- [ ] **Step 2–4:** Fail, implement, pass.
- [ ] **Step 5:** Commit `feat: verdict rules`.

### Task 6: Settings and device list

**Files:** Modify `Abstractions.cs`; Create `Settings.cs`; Test `SettingsStoreTests.cs` (temp directory per test).

**Interfaces — Produces:**
```csharp
public interface ISecretProtector { string Protect(string plain); string? Unprotect(string stored); } // null if it cannot be read
public enum ServerMode { Auto, Vibepollo, Sunshine, None }
public sealed class AppSettings {
    public Thresholds Thresholds { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool Notifications { get; set; }            // default false
    public ServerMode ServerMode { get; set; }         // default Auto
    public int ServerPort { get; set; } = 47990;
    public string? ProtectedToken { get; set; }
    public List<Device> Devices { get; set; } = [];
}
public sealed class SettingsStore(string directory) {
    public AppSettings Load();     // defaults if missing; on unreadable JSON, move file to settings.json.bad and return defaults
    public void Save(AppSettings s); // write to temp file then replace
}
```

- [ ] **Step 1: Failing tests** — `Missing_file_gives_defaults` (port 47990, notifications false, mode Auto), `Round_trip_preserves_devices_and_thresholds`, `Corrupt_file_gives_defaults_and_keeps_bad_copy` (RF 4: content `{"Devices": [`), `Empty_file_gives_defaults` (RF 4), `Unknown_properties_are_ignored`, `Save_creates_directory`.
- [ ] **Step 2–4:** Fail, implement, pass.
- [ ] **Step 5:** Commit `feat: settings and device list storage`.

### Task 7: History storage

**Files:** Create `History.cs`; Test `HistoryTests.cs` (SQLite file in temp dir). Add package `Microsoft.Data.Sqlite` to Core.

**Interfaces:**
- Consumes: `TargetStats`, `AdapterStats`.
- Produces:
```csharp
public sealed record TargetBucket(DateTimeOffset Start, double? MinMs, double? AvgMs, double? MaxMs, double? JitterMs, double LossPct, bool Offline);
public sealed record AdapterBucket(DateTimeOffset Start, double SendBps, double ReceiveBps, long OutErrors, long OutDiscards, long LinkSpeedBps);
public sealed record SessionRecord(long Id, string ClientName, string? Address, DateTimeOffset Start, DateTimeOffset? End);
public sealed class BucketAggregator(TimeSpan size) {          // 5 s
    public TargetBucket? Add(DateTimeOffset now, TargetStats s);   // returns a finished bucket when `now` crosses a boundary
    public AdapterBucket? Add(DateTimeOffset now, AdapterStats s);
}
public sealed class HistoryStore(string dbPath) : IDisposable {
    public void AppendTarget(Guid deviceId, TargetBucket b);
    public void AppendAdapter(AdapterBucket b);
    public long OpenSession(string clientName, string? address, DateTimeOffset start);
    public void CloseSession(long id, DateTimeOffset end);
    public void CloseOpenSessions(DateTimeOffset end);            // called at startup
    public IReadOnlyList<TargetBucket> QueryTarget(Guid deviceId, DateTimeOffset from, DateTimeOffset to);
    public IReadOnlyList<AdapterBucket> QueryAdapter(DateTimeOffset from, DateTimeOffset to);
    public IReadOnlyList<SessionRecord> QuerySessions(DateTimeOffset from, DateTimeOffset to);
    public int Prune(DateTimeOffset now);                         // deletes rows older than 14 days, returns count
}
```
Timestamps stored as UTC Unix milliseconds. WAL journal mode. Writes batched in one transaction per tick.

- [ ] **Step 1: Failing tests** — `Aggregator_emits_bucket_every_5s_with_min_avg_max`, `Aggregator_bucket_start_is_aligned_to_5s`, `Append_then_query_round_trips`, `Query_respects_range`, `Prune_removes_only_rows_older_than_14_days`, `Session_open_close_round_trips`, `CloseOpenSessions_closes_leftovers`, `Reopening_existing_db_keeps_data`.
- [ ] **Step 2–4:** Fail, implement, pass.
- [ ] **Step 5:** Commit `feat: SQLite history with 5 s buckets and pruning`.

### Task 8: Streaming-server link

**Files:** Create `StreamServer.cs`, `ServerParsers.cs`; Test `StreamServerLinkTests.cs`, `Fixtures/*.json`; add `FakeHttpHandler` to `Fakes.cs`.

**Interfaces:**
- Consumes: `ServerMode`.
- Produces:
```csharp
public enum ServerKind { None, Sunshine, Vibepollo }
public sealed record StreamSession(string ClientName, string? Address, DateTimeOffset? StartedAt);
public sealed record ServerStatus(ServerKind Kind, bool Running, IReadOnlyList<StreamSession> Sessions,
                                  IReadOnlyList<string> PairedNames, string? Note,
                                  IReadOnlyDictionary<string, string> HostStats) {
    public static ServerStatus Absent { get; }
}
public interface IStreamServer { Task<ServerStatus> PollAsync(CancellationToken ct); }
public sealed class StreamServerLink(HttpMessageHandler handler, IClock clock,
        Func<ServerMode> mode, Func<int> port, Func<string?> token) : IStreamServer;
public static class VibepolloParser { public static IReadOnlyList<StreamSession> Sessions(string json); public static IReadOnlyList<string> PairedNames(string json); }
public static class SunshineParser  { public static IReadOnlyList<string> PairedNames(string json); }
```
Behaviour:
- Detection (cached 30 s): GET `/api/session/status` — any response other than 404/connection failure → `Vibepollo`; else GET `/api/clients/list` answers (any status but connection failure) → `Sunshine`; else `None`. Mode `None` makes no requests; a forced mode skips detection.
- Vibepollo poll: `/api/rtsp/sessions` (fall back to `/api/session/status`) → sessions; `/api/clients/list` → paired names. Token sent as `Authorization: Bearer <token>` when present.
- Host statistics: `/api/host/stats` every 5 s (Vibepollo only); `HostStats` holds its top-level scalar values as strings, empty on any failure. The Live view lists them under the adapter line; Task 11 picks which to keep once the real response is seen.
- Notes (exact): no token in Vibepollo mode → `Add an access token in Settings to see live sessions`; 401/403 → `Access token was rejected`; non-JSON or unexpected shape → `Server link unavailable`; timeout (2 s) or connection failure → `Server link unavailable`.
- Parsers are tolerant: walk the JSON for an array of objects; client name from the first of `name`, `client_name`, `device_name`; address from the first of `address`, `ip`, `client_ip`, `remote_address`, `peer`; start from `started_at`, `start_time` (ISO string or Unix seconds). Missing fields → null; a non-array/non-object document → empty list. Strip a `:port` suffix and IPv6 brackets from addresses. **These field names are assumptions**: Task 11 replaces the fixtures with real responses and adjusts the lists.
- `PollAsync` never throws except for cancellation.

- [ ] **Step 1: Failing tests**
```csharp
Detects_vibepollo_when_session_endpoint_exists()
Detects_sunshine_when_only_clients_list_exists()
Detects_none_when_nothing_listens()
Mode_None_makes_no_requests()
Forced_mode_skips_detection()
Detection_is_cached_for_30s()
Sends_bearer_token_and_only_GETs_to_localhost()
Parses_sessions_with_name_and_address()            // fixture vibepollo-sessions.json
Address_port_suffix_is_stripped()                  // "192.168.1.40:51234" → "192.168.1.40"
Missing_token_gives_note_and_no_sessions()
Rejected_token_gives_note()                        // 401            (RF 5)
Html_login_page_gives_unavailable_note()           // 200 text/html  (RF 5)
Unexpected_json_shape_gives_empty_sessions()       // "{\"ok\":true}" (RF 5)
Timeout_gives_unavailable_note_and_does_not_throw()
Host_stats_top_level_scalars_are_exposed_and_polled_every_5s()
```
- [ ] **Step 2–4:** Fail, implement, pass.
- [ ] **Step 5:** Commit `feat: streaming-server link with Vibepollo, Sunshine and none modes`.

### Task 9: Monitor engine

**Files:** Modify `Abstractions.cs`; Create `MonitorEngine.cs`; Test `MonitorEngineTests.cs`.

**Interfaces:**
- Consumes: everything above.
- Produces:
```csharp
public interface INeighbourSource { IReadOnlyList<(string Address, string? Name)> Neighbours(); string? DefaultGateway(); }
public sealed record Snapshot(DateTimeOffset At, Verdict Verdict, AdapterStats? Adapter,
    IReadOnlyList<TargetState> Targets, ServerStatus Server, IReadOnlySet<Guid> SessionDeviceIds);
public sealed class MonitorEngine(AppSettings settings, IPinger pinger, IAdapterSource adapter,
        IStreamServer server, INeighbourSource neighbours, HistoryStore history, IClock clock) {
    public Snapshot Current { get; }
    public event Action<Snapshot>? Updated;
    public Task TickAsync(CancellationToken ct);   // one second's work; the app calls it from a 1 s timer
    public void ReloadDevices();                   // after the device list is edited
    public Device PinSessionDevice(Guid id, ConnectionType type);   // moves a session device into settings.Devices
}
```
Rules per tick: probe all targets concurrently; update adapter; poll server at its intervals (sessions 2 s) without blocking the tick beyond its 2 s timeout; evaluate verdict; feed aggregators and write finished buckets; raise `Updated`. The default gateway is always a target (`Role = Gateway`, name `Gateway`) unless a saved device has the same host. A session with an address not matching any saved device host becomes a session device (`StreamingDevice`, `WiFi`, name = client name); it is removed when the session ends. A session whose address matches a saved device adds nothing. Session start/end are written to history. A session with no address: matched to a saved device by name (case-insensitive), otherwise recorded in history only. If a tick is still running when the next is due, the next is skipped.

- [ ] **Step 1: Failing tests** — `Gateway_is_added_automatically`, `Gateway_not_duplicated_when_saved`, `Session_adds_device_and_end_removes_it`, `Session_matching_saved_address_is_not_duplicated`, `Session_without_address_matches_by_name`, `Session_start_and_end_written_to_history`, `Server_failure_does_not_stop_probing` (server fake throws), `Pin_moves_session_device_into_settings`, `Buckets_written_every_5s`, `Updated_raised_each_tick`.
- [ ] **Step 2–4:** Fail, implement, pass.
- [ ] **Step 5:** Commit `feat: monitor engine`.

### Task 10: Windows app — sources, Live view, tray

**Files:** Create under `src/StreamNetMon.App/`: `Windows/IcmpPinger.cs`, `Windows/WindowsAdapterSource.cs`, `Windows/NeighbourTable.cs`, `Windows/DpapiProtector.cs`, `Windows/SystemClock.cs`, `ViewModels/MainViewModel.cs`, `ViewModels/LiveViewModel.cs`, `Views/LiveView.xaml(.cs)`; Modify `App.xaml.cs`, `MainWindow.xaml(.cs)`. Packages: `ScottPlot.WPF`, `H.NotifyIcon.Wpf`.

**Interfaces — Consumes:** `MonitorEngine`, `Snapshot`, the Core interfaces.

- `IcmpPinger`: `System.Net.NetworkInformation.Ping.SendPingAsync`; reply status other than `Success` → null.
- `WindowsAdapterSource`: the `Up` interface that owns the default-gateway route (not loopback, not virtual/tunnel); `GetIPStatistics()` for counters, `Speed` for link speed.
- `NeighbourTable`: IPv4 neighbour entries via `GetIpNetTable2` (P/Invoke); names via reverse DNS with a 500 ms cap, done off the UI thread and cached.
- `DpapiProtector`: `ProtectedData` with `DataProtectionScope.CurrentUser`, base64.
- `App`: single instance (named mutex); data dir `%LOCALAPPDATA%\StreamNetMon`; builds engine; 1 s `PeriodicTimer` on a background task; `HttpClientHandler` whose certificate callback accepts only when the request host is `localhost`.
- Window: header (verdict dot + message, detected server and "streaming to {names}", send rate), device table (name, type, latency, jitter, loss, 60 s sparkline; offline rows greyed), adapter line, 10-minute throughput chart, tab strip Live / History / Devices / Settings. Closing hides to tray. Tray icon colour by `VerdictLevel` (green/amber/red); menu: Open, Exit. Charts update only when the window is visible.
- Formatting: latency/jitter one decimal + ` ms`; loss one decimal + `%`; rates auto-scaled `Kbps`/`Mbps`/`Gbps`.

- [ ] **Step 1:** Add a small Core helper `Format` (`Rate(double bps)`, `Ms(double?)`, `Pct(double)`) with tests `Rate_scales_units` (38_200_000 → `38.2 Mbps`), `Ms_null_is_dash`. Fail, implement, pass.
- [ ] **Step 2:** Implement the Windows sources and the app shell.
- [ ] **Step 3:** Container: `dotnet build src/StreamNetMon.App -p:EnableWindowsTargeting=true` — Expected: Build succeeded.
- [ ] **Step 4:** Stella: fetch the branch, `dotnet build`, start the app. Expected: window shows `Ethernet 2.5 Gbps`, a `Gateway` row with latency under 2 ms, verdict `All clear`, tray icon present. Record what was observed.
- [ ] **Step 5:** Commit `feat: Windows app with live view and tray icon`.

### Task 11: Devices, Settings, History, notifications, real server responses

**Files:** Create `Views/DevicesView.xaml(.cs)`, `Views/SettingsView.xaml(.cs)`, `Views/HistoryView.xaml(.cs)` + view models, `Windows/StartupRegistration.cs`, `Windows/Toasts.cs`; Modify `ServerParsers.cs`, `Fixtures/*.json`.

- **Devices:** list with add/edit/remove; "Add device" lists `INeighbourSource.Neighbours()` plus paired names from the server for labelling; fields name, host, type, role; a session device row has a "Keep" button → `PinSessionDevice`.
- **Settings:** thresholds; Start with Windows (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `StreamNetMon`); Notifications; server mode, port, token (password box; shows `Saved` plus the last 4 characters only; "Clear" button). "Save server responses for diagnostics" writes each polled endpoint's body to `%LOCALAPPDATA%\StreamNetMon\diagnostics\<endpoint>.json`, never the token or request headers.
- **History:** device picker, range (1 h / 6 h / 24 h / 7 d / custom), stacked charts for latency, jitter, loss and send rate on one time axis, sessions as shaded bands labelled with the client name.
- **Toasts:** when enabled, one toast when the verdict has been other than `All clear` for more than 10 s; none again until it has returned to `All clear`. Put the timing in a Core class `AlertGate { bool ShouldNotify(Verdict v, DateTimeOffset now) }` with tests `Fires_once_after_10s`, `Does_not_fire_for_short_blips`, `Rearms_after_recovery`.
- **Real responses (needs the user):** ask the user to create a read-only token in Vibepollo, paste it into Settings, start a stream, and press "Save server responses". Read the diagnostics files through Desktop Commander, replace the assumed fixtures with them (addresses and names anonymised), and fix the parser field lists until the tests pass against real data. If sessions carry no client address, keep name matching and note it in the README. Repeat detection with Vibepollo stopped.

- [ ] **Step 1:** `AlertGate` tests fail → implement → pass.
- [ ] **Step 2:** Implement the three views, startup registration and toasts; container build succeeds.
- [ ] **Step 3:** Stella: add a device from the picker; it appears in Live and survives restart. Turn the device's network off: row goes offline within 60 s, verdict stays `All clear`.
- [ ] **Step 4:** Real-response step above; parser tests pass against recorded fixtures.
- [ ] **Step 5:** Commit `feat: devices, settings, history, notifications; parsers match real server responses`.

### Task 12: Footprint, packaging, docs

- [ ] **Step 1:** Stella, window hidden, 10 minutes: read CPU time and working set of the process (`Get-Process StreamNetMon`). Expected: average CPU under 1%, working set under 150 MB. If over, fix (most likely chart or timer work while hidden) and re-measure.
- [ ] **Step 2:** `dotnet publish src/StreamNetMon.App -c Release -r win-x64 --self-contained false -o publish` on Stella; start `publish\StreamNetMon.exe`; it runs.
- [ ] **Step 3:** README: what it does, how to read the verdict, adding devices, creating the token, switching servers, where data lives, build/run/publish commands, known limits (no client Wi-Fi signal, adapter-level throughput, Sunshine mode is reduced).
- [ ] **Step 4:** `dotnet test` — all pass. Commit `docs: README and publish instructions`; update the PR title and description.
