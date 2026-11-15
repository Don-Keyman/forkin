# Stream network monitor — design

Date: 2026-11-14
Status: approved and built. Where the build differs from this design, see
"What changed after building" at the end; that section wins.

## Intent

The Windows desktop "Stella" hosts a game-streaming server (Vibepollo, a
Sunshine/Apollo fork, installed at `C:\Program Files\Apollo`). It streams to
devices inside the house: an iPad and Android devices, each sometimes wired and
sometimes on Wi-Fi through a router upstairs.

When a stream stutters, there is currently no way to tell whether the cause is
the desktop, the wired run, the router, or the Wi-Fi hop. This app runs on the
desktop alongside the streaming server and answers that question, live and in
hindsight.

## Goals

- Show the quality of the connection from the desktop to each streaming device:
  latency, jitter, packet loss.
- Show the same for the upstairs router and the default gateway, so a bad Wi-Fi
  hop can be told apart from a bad wired path.
- Show the desktop's own outgoing side: send throughput, adapter link speed,
  send errors and discards.
- Give a plain-language verdict ("Wi-Fi hop to iPad is degraded; router and
  desktop are fine").
- Keep history so a past stutter can be looked up by time.
- Pick up streaming sessions from the streaming server when it offers them, so
  devices appear on their own and history is marked per session.
- Keep working, unchanged, if the server is swapped for Sunshine or another
  fork, or is not running at all.
- Stay light: it runs next to a GPU- and CPU-heavy stream.

## Non-goals

- Security, intrusion detection, or inspecting other devices' traffic.
- Anything outside the house (internet upload, WAN routes).
- Reading Wi-Fi signal strength from client devices. The desktop cannot see it;
  only its effect (latency, jitter, loss) is measured.
- Per-process traffic breakdown (what share of throughput is the stream). This
  needs administrator-level tracing; adapter-level throughput is used instead.
- Controlling or configuring the streaming server.
- macOS/Linux support.

## Anti-goals

- Measurably hurting stream performance (CPU spikes, GPU use, network chatter).
- Requiring administrator rights to run.
- Opening any listening port on the network.
- Reading the streaming server's credentials or state files, or asking for its
  password. Access is by a token the user creates and pastes into the app.
- Depending on the streaming server: network measurement must never break
  because the server changed, updated, or is absent.

## Constraints

- Windows 11 Pro, Realtek 2.5 GbE adapter (currently linked at 2.5 Gbps).
- .NET 10 SDK (10.0.300) is installed on Stella; git, Node and Python also.
- The development container is Linux and cannot run a WPF window. UI and
  live-network behaviour are verified on Stella through Desktop Commander.
  Remote commands there return after 3 seconds, so builds and test runs must be
  started in the background and their output read afterwards.
- `config\session_history` under the Apollo folder is access-denied to a normal
  user; `config\logs` is readable.

## Approaches considered

1. **Native Windows app, .NET 10 + WPF — chosen.** Lightest footprint next to
   a stream; ping, adapter counters and IP statistics come straight from
   `System.Net.NetworkInformation` with no admin rights; SDK already on the
   machine. Cost: replaces the Node/TypeScript scaffold (a placeholder).
2. **Electron (Node/TypeScript).** Keeps the scaffold and makes charts easy,
   but runs a browser engine beside the stream and has to shell out to Windows
   command-line tools for its numbers.
3. **Background service + browser dashboard.** Light and viewable from the
   iPad, but not a desktop app and needs an open port.

The user picked option 1 and agreed to replace the scaffold.

## Design

### What is measured

| Target | Metrics | How |
|---|---|---|
| Each saved device | latency, jitter, loss | ICMP echo, 1 per second, 1 s timeout |
| Upstairs router, default gateway | latency, jitter, loss | same |
| Desktop adapter | bytes sent/received per second, link speed, outbound errors, outbound discards | `NetworkInterface` statistics, sampled once per second |

- **Latency**: round-trip time of each reply.
- **Jitter**: mean absolute difference between consecutive round-trip times
  over a rolling 30-second window.
- **Loss**: share of probes without a reply over the same window.
- A device that has not answered for 60 seconds is shown as **offline**, not as
  100% loss, and raises no alert (tablets sleep).

### Devices

- A saved list, edited in the app. Each entry: name, IP address or hostname,
  connection type (wired / Wi-Fi), and role (streaming device / router).
- The same physical device on wired and on Wi-Fi has a different address, so it
  is two entries ("iPad wired", "iPad Wi-Fi"). This is what makes the two
  comparable.
- An "Add device" picker lists addresses from the Windows neighbour table
  (devices the desktop has recently seen on the LAN) so nobody has to type IPs.
- The default gateway is added automatically.
- When the streaming-server link (below) reports a live session, that client is
  added automatically as a session device, labelled with its paired name, and
  probed for as long as the session lasts. Session devices are not saved to the
  list unless the user pins them; a pinned device gets a connection type.
- A session device whose address matches a saved entry is shown as that entry,
  not twice.

### Streaming-server link

A swappable piece behind one interface (`IStreamServer`): "is a server running,
which one, who is streaming now (name, address, start time), what does the host
report about itself". Network measurement never depends on it.

| Mode | Used when | Provides |
|---|---|---|
| Vibepollo / Apollo | its extended endpoints answer | live sessions with client name and address, host statistics, session start/end |
| Sunshine | only the stock Sunshine endpoints answer | whether it is running, paired device names for the "Add device" picker |
| None | no server found, or no token | nothing; saved list only |

- **Detection**: on startup and every 30 seconds, look for the server's local
  web interface on `https://localhost:47990` (port editable) and check which
  endpoints exist. Mode is chosen automatically; settings has a manual
  override (Auto / Vibepollo / Sunshine / None).
- **Endpoints seen on Stella's Vibepollo install** (listed from its web assets,
  responses not yet seen): `/api/session/status`, `/api/rtsp/sessions`,
  `/api/host/stats`, `/api/history/sessions`, `/api/clients/list`,
  `/api/token`, `/api/token/routes`.
- **Access**: the user creates an access token in the server's web page,
  limited to read-only endpoints, and pastes it into the app's Settings screen.
  It is stored encrypted with Windows DPAPI for the current user, never written
  to logs, and never shown again in full. Stock Sunshine has no tokens; for it
  the Sunshine mode works without login where it can, and otherwise reports
  "running" from the process list only.
- **Local certificate**: the server uses a self-signed certificate. The app
  accepts it for `localhost` only.
- **Polling**: session status every 2 seconds, host statistics every 5 seconds.
  Read-only requests only; the app never calls an endpoint that changes the
  server.
- **Failure**: any error, timeout or unexpected response drops the link to
  "None" for that cycle and shows a small "server link unavailable" note in the
  status bar. It never raises a verdict or stops probing.
- **Sessions in history**: each session is stored with client name, address,
  start and end, and drawn as a shaded band on the History timeline.

### Verdict

One line at the top, recomputed every second from the rolling windows:

- adapter link speed below its usual value, or outbound errors rising →
  **"Desktop adapter problem"**
- gateway/router degraded → **"Network path to router is degraded"**
- router fine, a Wi-Fi device degraded → **"Wi-Fi hop to <device> is degraded"**
- a wired device degraded, router fine → **"Wired path to <device> is degraded"**
- otherwise → **"All clear"**

"Degraded" defaults (editable in settings): loss ≥ 1%, or jitter ≥ 5 ms wired /
≥ 15 ms Wi-Fi, or latency ≥ 10 ms wired / ≥ 30 ms Wi-Fi.

### Window

```
┌ Stream network monitor ───────────────────────────── ─ □ x ┐
│ ● All clear                                    ↑ 38.2 Mbps │
├────────────────────────────────────────────────────────────┤
│ Device           Type   Latency  Jitter  Loss   Last 60 s  │
│ ● Router (up)    wired   0.4 ms  0.1 ms  0.0%   ▁▁▁▁▁▁▁▁▁  │
│ ● iPad wired     wired   0.9 ms  0.2 ms  0.0%   ▁▁▁▂▁▁▁▁▁  │
│ ● iPad Wi-Fi     Wi-Fi  14.0 ms 22.0 ms  2.1%   ▂▅▇▃▆▂▇▅▃  │
│ ○ Shield         wired  offline                            │
├────────────────────────────────────────────────────────────┤
│ Desktop: Ethernet 2.5 Gbps · send errors 0 · discards 0    │
│ [ throughput chart, last 10 min ]                          │
├────────────────────────────────────────────────────────────┤
│ [Live] [History] [Devices] [Settings]                      │
└────────────────────────────────────────────────────────────┘
```

- **Live**: the table and chart above.
- **History**: pick a device and a time range; charts of latency, jitter, loss
  and desktop throughput on a shared time axis.
- **Devices**: the saved list and the "Add device" picker.
- **Settings**: thresholds, start with Windows, notifications on/off, streaming
  server mode, port and access token.
- The header shows the detected server and, while streaming, who is connected
  ("Vibepollo · streaming to iPad").
- **Tray icon**: green / amber / red following the verdict. Closing the window
  minimises to the tray; "Exit" in the tray menu quits.
- **Notification**: one Windows toast when the verdict leaves "All clear" for
  more than 10 seconds, and none again until it has recovered. Off by default,
  because a toast popping over a stream can be worse than the problem — see
  "Calls".

### Storage

- SQLite file in `%LOCALAPPDATA%\StreamNetMon\`.
- One row per target per 5 seconds (min/avg/max latency, jitter, loss, and for
  the adapter: bytes sent/received, errors, discards).
- Rows older than 14 days are deleted at startup and once a day.
- Device list and settings in a JSON file in the same folder.

### Structure

```
StreamNetMon.sln
src/StreamNetMon.Core/     net10.0   probing, rolling statistics, verdict,
                                     storage, settings. No UI, no Windows-only
                                     APIs behind its interfaces.
src/StreamNetMon.App/      net10.0-windows  WPF window, tray icon, toasts,
                                     Windows-specific sources (neighbour table).
tests/StreamNetMon.Core.Tests/  xUnit
```

- Core takes its inputs through interfaces (`IPinger`, `IAdapterSource`,
  `IStreamServer`, `IClock`) so the statistics, verdict and storage are tested with fakes and
  need no network.
- The existing Node/TypeScript files (`package.json`, `tsconfig*.json`, `src/`,
  `test/`, `.nvmrc`, lockfile) are removed; `.gitignore` and `README.md` are
  rewritten for .NET.

### Footprint

Stay light next to a stream: under 1% CPU on average; no GPU-accelerated
animation; charts redraw at most once per second and not at all while the
window is hidden. Probe traffic is one small ICMP packet per target per second.

There is no memory limit. An earlier version of this design set one at 150 MB;
that was the designer's number, not the user's, and the user dropped it: for
home use, memory around the measured level (about 216 MB, flat, no leak) is
fine. Memory only matters if it keeps growing.

## Calls made by the designer (not decided by the user)

- App name "Stream network monitor" / `StreamNetMon`.
- Saved devices are always probed, streaming or not. At 1 packet per second
  this costs nothing and gives a baseline for when nothing is streaming.
- The streaming-server link reads the server's web interface, not its log files
  or its locked session folder.
- Polling intervals, the localhost-only certificate exception, and DPAPI for
  the token.
- Toast notifications exist but are **off by default**; the tray icon colour is
  the default signal.
- Threshold defaults, the 30-second window, 5-second storage resolution and
  14-day retention.
- Does not start with Windows unless switched on in settings.

## Left to the builder

- Charting library (ScottPlot for WPF is the suggested default).
- Tray icon implementation (a maintained NuGet package or `NotifyIcon` interop).
- SQLite access library and schema details.
- Exact layout, colours and icon, within the sketch above.
- The response shapes of the Vibepollo endpoints, and what stock Sunshine
  answers without a token. These must be read from a real server on Stella
  (with a token pasted into the app, and a stream running) before the Vibepollo
  mode is called done. If live sessions turn out not to include the client
  address, say so and fall back to matching by paired name against saved
  devices.
- How to package for the user (a self-contained published folder is enough; no
  installer required).

## Verification

- Unit tests for rolling statistics, the verdict rules, offline handling and
  storage pruning, run with `dotnet test` on Stella.
- On Stella: the app builds, starts, shows the adapter at its real link speed,
  and shows the default gateway with sub-millisecond-range latency.
- With a real device added: unplugging it or turning its Wi-Fi off turns it
  offline within 60 seconds without an alert.
- Idle footprint measured on Stella: CPU against the figure above, and memory
  checked for steady growth only.
- Unit tests for mode detection and for each mode against recorded responses,
  including malformed and failing ones.
- On Stella with a token: the header names the server; starting a stream makes
  the client appear within a few seconds and ending it closes the session band.
- With the server stopped, and with the mode forced to None: probing, verdict
  and history carry on unchanged.

## What changed after building

Found on Stella with the real server and the real network. Where this section
and the design above disagree, this section is what the app does.

### The house has two networks, not one

```
Stella ─wired─▶ provider's box ─▶ upstairs router ─wired or Wi-Fi─▶ iPad and
192.168.2.67    192.168.2.254     (ASUS, own network                other streaming
                (Stella's         192.168.71.x)                     devices
                 gateway)
```

- The provider's box passes everything through to the upstairs router, which
  runs its own network. The streaming devices live on that second network.
- From Stella, nothing past the provider's box answers a ping: not the upstairs
  router, not the devices behind it. The cabling stays as it is.
- So the per-device rows in the design ("iPad wired", "iPad Wi-Fi", "Router
  (up)") cannot be measured in this house. The saved list holds only the
  gateway. Saved devices and their verdicts still work for anything that is on
  Stella's own network.

### Vibepollo gives no client address

- Live sessions carry a paired name and stream figures, but no address. Session
  devices are therefore not added and probed automatically.
- A stream is tied to a saved device by paired name instead (exact, or the name
  followed by a space, `-`, `_` or `(`), preferring a device that is online.
- Token scopes in use, all read-only: `/api/session/status`,
  `/api/rtsp/sessions`, `/api/clients/list`, `/api/host/stats`.

### Per-stream figures and the "past the router" verdict

- The Live tab shows one line per running stream: picture size, codec, frame
  rate, the encoder's bitrate setting ("encoder set to …", which is the setting
  and not the send rate), losses the client reports, and frame recoveries (the
  client asking for a fresh full frame).
- Those figures are the only view of the far side, so they feed a new verdict:
  **"Stream to <device> has trouble past the router"**. It fires when a stream
  reports 2 or more new losses, or 3 or more new frame recoveries, within 30
  seconds, while this PC and its router look healthy. Counters going backwards
  mean a new stream and start the count over.
- Verdicts in rank order: Desktop adapter problem · Network path to router is
  degraded · Wi-Fi hop to <device> is degraded · Wired path to <device> is
  degraded · Stream to <device> has trouble past the router · All clear.
- A router that answered before and goes silent counts as degraded; one that
  has never answered ping is ignored. No network adapter at all counts as a
  desktop adapter problem.

### Builder's choices

- Charts are small custom controls and the tray icon is the WinForms
  `NotifyIcon`, in place of the suggested packages: no extra dependencies.
- History range is presets (1 h / 6 h / 24 h / 7 d) with Earlier / Later / Now.
- Notifications stay off by default; the user confirmed this.

### Still untested

- The "past the router" verdict firing on a stream that is really struggling.
- Sunshine mode (no Sunshine install).
- A saved device going offline and coming back, on real hardware.
