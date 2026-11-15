# active-memory handoff: Stream network monitor (forkin)

**Handoff #1** · 2026-11-15 · Lineage: #1 (2026-11-15): designed, planned and built a Windows tray app that monitors streaming-host connection quality; running on the user's PC and reading Vibepollo's real data

## 0. Instructions for Claude (read first)

You are continuing work from a previous chat. That chat is gone; this file is the complete context and the source of truth.

1. Read this whole file before replying.
2. Follow sections 3 (Style), 4 (Hard rules) and 5 (Corrections) in every reply, for the rest of this chat. They override your defaults.
3. Use the values in section 8 exactly. Never round, re-estimate, or "correct" them.
4. Do not suggest anything listed in section 7 (Changed / rejected) again unless the user brings it up.
5. Code word: None active. If the user types /amcodeword, start every reply with the phrase they choose (default "Yes Boss!").
6. Your first reply: at most 5 lines covering the goal, the current state, and the next step (section 11). Mention any files from section 13 that were not attached. Ask the questions in section 12 if there are any. End with "Ready to continue with <next step>?" Then wait for the user's go.

## 1. Mission
- **Goal:** A Windows desktop app ("Stream network monitor", `StreamNetMon`) that runs on the user's streaming host PC beside Vibepollo and shows the quality of the connection going out to the devices it streams to inside the house.
- **Done looks like:** It tells the user whether a bad stream is caused by the desktop, the wired run, the router, or the Wi-Fi hop; shows per-device latency, jitter and loss; keeps history; stays light next to a stream.
- **Why it matters / context:** The PC hosts a local game-streaming server and home network performance matters to the user. Not a security tool.

## 2. About the user (as relevant to this work)
- Runs a home streaming setup; host PC is named "Stella", user profile `C:\Users\zoso7`.
- Streams to an iPad and Android devices, each sometimes hardwired and sometimes on Wi-Fi through a router upstairs. All inside the house.
- Uses Vibepollo (a Sunshine/Apollo fork; they also call it "vibeshine" / "foundation sunshine" and say "they're all forked from sunshine so I get confused"). May switch to Sunshine later.
- Talks about outcomes, not implementation; hands technical choices to Claude. Writes short messages.
- Has Remote Desktop Commander installed on Stella, and has "local control over vibepollo".
- GitHub: `Don-Keyman/forkin`.

## 3. Style & communication
- **Language:** English.
- **Tone:** Plain, direct, no jargon; explain technical terms.
- **Reply length:** Short to medium; lead with what matters.
- **Formatting:** Markdown, short bullets and small tables are fine. Text sketches of screens were well received.
- **Working style:** One question at a time, with the reason for asking. State calls made on the user's behalf out loud. The user approved design and plan before building. Plan execution is "native" (Claude does every task itself, no subagents).
- **Avoid:** Asking the user to paste secrets into chat. Getting hung up on wording ("signal").

## 4. Hard rules (word for word)
1. "Not worried about malicious interference" (it is not a security or intrusion tool).
2. "it will be all inside the house" (no internet/WAN monitoring).
3. "I guess I should say quality of the connection? I don't want to get too hung up on the word signal."
4. "Option one and it's fine to replace the earlier start" (.NET/WPF; the Node/TypeScript scaffold may be replaced).
5. "Go with native" (execute the plan inline, no subagents).
6. The access token is pasted into the app's Settings screen, never into chat (Claude's rule, accepted by the user).
7. The app only reads from the streaming server: GET requests, to this PC only. It never changes the server.
8. Never read `C:\Program Files\Apollo\config\credentials`, `*_state.json`, or `session_history`.
9. Session rule: do not spawn subagents unless the user asks.
10. Develop on branch `claude/keen-dirac-nr178b`; push only there; PRs are drafts.

## 5. Corrections log
| # | Claude did | The user wanted |
|---|---|---|
| 1 | Framed the app around "signal quality" | "quality of the connection"; the host is on Ethernet |
| 2 | Assumed "vibeshine"/"foundation sunshine" | The server is "Vibepollo"; all are Sunshine forks |
| 3 | Assumed a "stats"-only token scope was the user's final choice | User made a new token with four GET scopes (section 8) |

## 6. Decisions
| Decision | Why |
|---|---|
| Native Windows app, .NET 10 + WPF, compact window + tray icon | Lightest next to a stream; user picked "Option one" |
| Replace the Node/TypeScript scaffold | User: "it's fine to replace the earlier start" |
| Ping each target once a second; 30 s rolling window; offline after 60 s | Design, approved |
| Saved device list, always probed; default gateway added automatically | Design, approved |
| Streaming-server link with modes Vibepollo/Apollo, Sunshine, None; auto-detect + manual override | User asked for it to be swappable |
| Token created in Vibepollo's web page, pasted into app Settings, stored with DPAPI | User agreed |
| Notifications off by default; tray icon colour is the signal | Claude's call, in approved design |
| Native (inline) plan execution | User: "Go with native" |
| Custom lightweight charts instead of ScottPlot; WinForms NotifyIcon for the tray | Claude's ruling: lower footprint, no dependencies |
| History range = presets (1 h / 6 h / 24 h / 7 d) + Earlier/Later/Now | Claude's ruling |
| Router/gateway that answered before and goes silent = "Network path to router is degraded"; one that never answered ping is ignored | Claude's ruling from self-review |
| No adapter at all = "Desktop adapter problem" | Claude's fix from self-review |
| Streams are tied to saved devices by paired name (exact, or name + space/-/_/( suffix), preferring the one that is online | Vibepollo gives no client address |
| Per-stream figures shown on Live tab | Claude's addition; Vibepollo reports them; told to the user |

## 7. Changed / rejected
- "monitors local network traffic and interference" → connection quality from the streaming host to in-home devices (user clarified).
- "signal" → "quality of the connection".
- Node/TypeScript scaffold → .NET 10 / WPF solution.
- ❌ Electron app (runs a browser engine beside the stream).
- ❌ Background service + browser dashboard (not a desktop app; opens a port).
- Auto-detect devices from server logs → saved list (logs unreliable, session folder locked).
- Auto-add streaming devices by address → name matching (real Vibepollo sessions carry no address).
- Token scope "stats" only → four GET scopes.
- ScottPlot.WPF / H.NotifyIcon.Wpf (in the plan) → custom chart controls / WinForms NotifyIcon.
- Stream label "500.0 Mbps" → "encoder set to 500.0 Mbps" (it is the encoder setting, not the send rate).
- ❌ Per-app traffic breakdown (needs administrator rights).
- ❌ Reading Wi-Fi signal strength from client devices (the host cannot see it).

## 8. Data & facts (exact)

**Repository**
| Item | Value |
|---|---|
| Repo | `Don-Keyman/forkin` |
| Branch | `claude/keen-dirac-nr178b` |
| PR | #1, draft: https://github.com/Don-Keyman/forkin/pull/1 |
| Latest commit | `45d3486` "fix: label the stream bitrate as the encoder setting, not the send rate" |
| Tests | 149 passing (`dotnet test`) |
| Design doc | `docs/superpowers/specs/2026-11-14-stream-network-monitor-design.md` |
| Plan | `docs/superpowers/plans/2026-11-14-stream-network-monitor.md` |

**Stella (the user's PC)**
| Item | Value |
|---|---|
| OS | Windows 11 Pro, 8 logical processors |
| Adapter | "Ethernet", Realtek PCIe 2.5GbE Family Controller, 2.5 Gbps |
| Gateway | 192.168.2.254 (about 1 ms, 0% loss) |
| Other addresses seen | 192.168.2.75, 192.168.2.76, 192.168.2.79 (not yet identified) |
| Streaming server | Vibepollo at `C:\Program Files\Apollo` (`sunshine.exe`, service "ApolloService"), web interface `https://localhost:47990` |
| Tools | .NET SDK 10.0.300, Node 24, Python, git 2.56.1 |
| Desktop Commander device | "Stella", ID `112fb53f-9e5e-448a-a2b4-245b72ca0b77` |
| Source clone | `C:\Users\zoso7\forkin` |
| Running app | `C:\Users\zoso7\forkin\publish\StreamNetMon.exe`, built from commit `6e54ef2` (one commit behind) |
| App data | `%LOCALAPPDATA%\StreamNetMon\` (`settings.json`, `history.db`, `log.txt`, `diagnostics\`) |
| Helper scripts | `%TEMP%\snm-build.ps1`, `snm-publish.ps1`, `snm-shot.ps1`, `snm-tab.ps1`, `snm-click.ps1`; log `%TEMP%\snm-build.log`; screenshot `%TEMP%\snm-shot.png` |

**Vibepollo token and API (verified against the real server)**
- Token: [secret removed: re-enter it]. Already saved in the app on Stella; sent as `Authorization: Bearer`. It works.
- Scopes (all GET): `/api/session/status`, `/api/rtsp/sessions`, `/api/clients/list`, `/api/host/stats`.
- Unauthenticated: known paths answer 401, unknown paths 404.
- `/api/rtsp/sessions` → `{"sessions":[{device_name, uuid, state, uptime_seconds, codec, width, height, hdr, fps, encoder_bitrate_kbps, requested_bitrate_kbps, client_reported_losses, idr_requests, invalidate_ref_count, bytes_sent, packets_sent, frames_sent, encode_latency_ms, ...}]}`. **No client address.**
- `/api/clients/list` → `{"named_certs":[{name, connected, last_seen, uuid, ...}]}`.
- `/api/host/stats` → `cpu_percent, cpu_temp_c, gpu_percent, gpu_encoder_percent, gpu_temp_c, net_rx_bps, net_tx_bps, ram_percent, ram_total_bytes, ram_used_bytes, vram_*`.
- `/api/session/status` → `{"activeSessions":1,"appName":"Desktop","appRunning":true,"lastEncoderProbeFailed":false,"paused":false,"status":true}`.
- Paired client names: `pp` (was streaming: 3840×2160 HEVC HDR, 120 fps), `V++`, `Lap`, `mooigjt enhanc`.

**App constants**
- Degraded defaults: loss ≥ 1%; jitter ≥ 5 ms wired / ≥ 15 ms Wi-Fi; latency ≥ 10 ms wired / ≥ 30 ms Wi-Fi.
- History: SQLite, 5 s buckets, 14-day retention. Server polling: detection 30 s, sessions 2 s, host stats 5 s; request timeout 2 s.
- Verdicts, exactly: `All clear`, `Desktop adapter problem`, `Network path to router is degraded`, `Wi-Fi hop to {name} is degraded`, `Wired path to {name} is degraded`.
- Footprint measured on Stella (Release, window hidden, 11 minutes): about 0.24% of one core; working set 96 → 112 MB; private memory 26 → 38 MB. Budget: 1% and 150 MB.

**Lessons that cost time**
- Windows takes 2048 ms to refuse an IPv6 loopback connect; the app uses `LoopbackConnector` (races IPv4/IPv6 loopback, refuses any other host).
- Stella's .NET had no NuGet source; the repo has a `nuget.config` pointing at nuget.org.
- Desktop Commander commands on Stella return after 3 seconds: start builds with `Start-Process powershell -File <script>` and read the log file afterwards. The shell is already PowerShell; do not wrap commands in `powershell -Command "..."` (it strips `$`).
- The cloud container has no .NET by default: install with `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /root/.dotnet`, then `export PATH=/root/.dotnet:$PATH`. Build the app there with `-p:EnableWindowsTargeting=true`.

## 9. People, terms & names
- **People:** the user (owner of Stella and the repo).
- **Terms & nicknames:** Stella = the Windows streaming host. Vibepollo = the streaming server in use (Sunshine/Apollo fork). "pp" = a paired streaming client name. "signal" = the user's loose word for connection quality.
- **Names in use:** solution `StreamNetMon.sln`; projects `src/StreamNetMon.Core`, `src/StreamNetMon.App`, `tests/StreamNetMon.Core.Tests`; exe `StreamNetMon.exe`; window title "Stream network monitor"; tabs Live / History / Devices / Settings; key types `MonitorEngine`, `StreamServerLink`, `VibepolloParser`, `VerdictEngine`, `HistoryStore`, `RollingWindow`, `AdapterTracker`, `LoopbackConnector`, `AlertGate`.

## 10. Work state
| Item | Status | Version / location | Notes |
|---|---|---|---|
| Design doc | final, approved | `docs/superpowers/specs/…-design.md` | Predates the "no client address" finding |
| Implementation plan | final, approved | `docs/superpowers/plans/…md` | Tasks 1–12 all worked |
| Core library + tests | done | commit `45d3486`, 149 tests | |
| WPF app (4 tabs, tray, notifications) | done | commit `45d3486` | Seen working on Stella via screenshots |
| Vibepollo link | working with real token | on Stella | Shows "Vibepollo · streaming to pp" and stream figures |
| App on Stella | running | built from `6e54ef2` | Needs rebuild for the "encoder set to" label |
| Saved devices on Stella | none added | — | Only the gateway is watched |
| README | done | repo root | |
| PR #1 | draft, open | no CI in the repo | Description says 141 tests; now 149 |
| Sunshine mode | untested | — | No Sunshine install |
| Device offline/online on real hardware | untested | — | Covered by unit tests only |
| Final review | self-review only | — | No independent reviewer ran |

Deferred minors: the first adapter reading after start records a 0 send rate (a dip in history); "Seen on your network" lists the gateway and shows blank names (router has no reverse DNS).

## 11. Next steps
1. **Next action:** On Stella, run `%TEMP%\snm-publish.ps1` (via `Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$env:TEMP\snm-publish.ps1"`), then screenshot with `snm-shot.ps1` and confirm the second stream the user offered to start shows as a second ▶ line.
2. Help the user add devices on the Devices tab, named after paired names (e.g. `pp wired`, `pp Wi-Fi`), once they say which of 192.168.2.75 / .76 / .79 is which; confirm the ▶ marker and the History session band.
3. Update the PR description (149 tests, real Vibepollo responses verified, per-stream figures).
4. Watch memory over a longer run (it grew 96 → 112 MB in 11 minutes; cause unknown).
5. Still untested: Sunshine mode; a device going offline and back on real hardware.
6. A check-in on PR #1 is scheduled for 2026-11-15T03:52:00Z in the old session; it will not carry over.

## 12. Open questions ⚠️
- Which device is at 192.168.2.75, 192.168.2.76 and 192.168.2.79, and is each wired or on Wi-Fi? (Claude offered to add them for the user.)
- Has the user started the second stream yet? ("I can pull another device up as well" — not confirmed done.)
- Should the helper scripts in Stella's `%TEMP%` be deleted when testing is finished?

## 13. Re-attach checklist
Nothing to attach. Everything is in the repository `Don-Keyman/forkin` on branch `claude/keen-dirac-nr178b`; this file is at the repo root as `handoff-stream-network-monitor.md`.

---
<sub>Audit: 13/13 sections · 10 rules · 3 corrections · 60+ data points · secrets removed: yes · generated by active-memory</sub>
