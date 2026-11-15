# active-memory handoff: Stream network monitor (forkin)

**Handoff #3** · 2026-11-15 · Lineage: #3 (2026-11-14 to 15, run in Claude Code directly on Stella): user dropped the 150 MB memory budget (never theirs; about 216 MB is fine), design doc given a "What changed after building" section, ASUS ping setting confirmed turned back off, git identity set for the repo, GitHub MCP tried and removed (not needed); no app code changed; #1 (2026-11-15): designed, planned and built a Windows tray app that monitors streaming-host connection quality; running on the user's PC and reading Vibepollo's real data; #2 (2026-11-15): rebuilt on Stella, confirmed two streams show and clear, found the iPad sits behind a second router Stella cannot ping through, added a "trouble past the router" verdict from the stream's own figures, measured memory flat at about 216 MB (over the 150 MB budget)

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
- **Done looks like:** It tells the user whether a bad stream is caused by the desktop, the wired run, the router, or the far side (upstairs router / Wi-Fi); shows latency, jitter and loss for what it can reach; keeps history; stays light next to a stream.
- **Why it matters / context:** The PC hosts a local game-streaming server and home network performance matters to the user. Not a security tool.

## 2. About the user (as relevant to this work)
- Runs a home streaming setup; host PC is named "Stella", user profile `C:\Users\zoso7`.
- Streams to an iPad and Android devices, each sometimes hardwired and sometimes on Wi-Fi, through a second router on another floor. All inside the house.
- Uses Vibepollo (a Sunshine/Apollo fork; they also call it "vibeshine" / "foundation sunshine" and say "they're all forked from sunshine so I get confused"). May switch to Sunshine later.
- Talks about outcomes, not implementation; hands technical choices to Claude. Writes short messages, often typed fast on a phone with typos; read for intent.
- Keeps the app in the tray: "if the stream is open id check reguarly byt mainly when issues arise".
- Has Remote Desktop Commander connected to Stella, and has "local control over vibepollo".
- GitHub: `Don-Keyman/forkin`.

## 3. Style & communication
- **Language:** English.
- **Tone:** Plain, direct, no jargon; explain technical terms.
- **Reply length:** Short to medium; lead with what matters.
- **Formatting:** Markdown, short bullets and small tables are fine. Text sketches of screens and network paths were well received.
- **Working style:** One question at a time, with the reason for asking. State calls made on the user's behalf out loud. The user approved design and plan before building. Plan execution is "native" (Claude does every task itself, no subagents). Test-first for code changes.
- **Avoid:** Asking the user to paste secrets into chat. Getting hung up on wording ("signal"). Asking the same question again after the user has said "no issue". Assuming a short message answers the last question asked (see corrections 6 and 7).

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
10. The work lives on branch `claude/keen-dirac-nr178b` (draft PR #1 is open from it); push there; PRs are drafts. A new cloud session may be assigned a different branch name: say so and keep using `claude/keen-dirac-nr178b`, as the user accepted in handoff #2.
11. "Use Remote Desktop Commander for this" (anything on Stella goes through the Remote Desktop Commander connector, device "Stella").
12. "ok, no issue, as least invasive as possible, defaulting to no notifications on by default."
13. "no i cant unfortunately, its a mess with them being separate floors of the house. it was set up that way with the modem (router) acts as pass through to the upstairs router" (Stella's cable stays in the provider's box; do not propose re-cabling).
14. "i have no wifi on stella" (Stella is on Ethernet only).

## 5. Corrections log
| # | Claude did | The user wanted |
|---|---|---|
| 1 | Framed the app around "signal quality" | "quality of the connection"; the host is on Ethernet |
| 2 | Assumed "vibeshine"/"foundation sunshine" | The server is "Vibepollo"; all are Sunshine forks |
| 3 | Assumed a "stats"-only token scope was the user's final choice | User made a new token with four GET scopes (section 8) |
| 4 | On "continue", looked only in the session's own branch and said there was no earlier work | The handoff and code are on branch `claude/keen-dirac-nr178b`; run `git fetch origin` and look at all branches first |
| 5 | Said Remote Desktop Commander was not available and proposed a new chat, twice | "Use Remote Desktop Commander for this". The connector's tools (`mcp__Remote_Desktop_Commander__*`) loaded on the next message; search for them again before saying they are missing |
| 6 | Read "thats the ipad on wifi" as naming which address the iPad has | "ah. inmeant its the only device on thenstream" |
| 7 | Wrote as if Stella's own link might involve Wi-Fi | "i mentioned in the previous chat that the connections going from my computer were only the ethernet coming in, i have no wifi on stella" |
| 8 | Suggested moving Stella's cable to the ASUS router | "no i cant unfortunately" (hard rule 13) |
| 9 | Asked twice whether the app stays open or in the tray | "no issue"; tray mostly, opened when issues arise |

## 6. Decisions
| Decision | Why |
|---|---|
| Native Windows app, .NET 10 + WPF, compact window + tray icon | Lightest next to a stream; user picked "Option one" |
| Replace the Node/TypeScript scaffold | User: "it's fine to replace the earlier start" |
| Ping each target once a second; 30 s rolling window; offline after 60 s | Design, approved |
| Saved device list, always probed; default gateway added automatically | Design, approved |
| Streaming-server link with modes Vibepollo/Apollo, Sunshine, None; auto-detect + manual override | User asked for it to be swappable |
| Token created in Vibepollo's web page, pasted into app Settings, stored with DPAPI | User agreed |
| Notifications off by default; tray icon colour is the signal | User confirmed in handoff #2 (hard rule 12) |
| Native (inline) plan execution | User: "Go with native" |
| Custom lightweight charts instead of ScottPlot; WinForms NotifyIcon for the tray | Claude's ruling: lower footprint, no dependencies |
| History range = presets (1 h / 6 h / 24 h / 7 d) + Earlier/Later/Now | Claude's ruling |
| Router/gateway that answered before and goes silent = "Network path to router is degraded"; one that never answered ping is ignored | Claude's ruling from self-review |
| No adapter at all = "Desktop adapter problem" | Claude's fix from self-review |
| Streams are tied to saved devices by paired name (exact, or name + space/-/_/( suffix), preferring the one that is online | Vibepollo gives no client address |
| Per-stream figures shown on Live tab | Claude's addition; Vibepollo reports them; told to the user |
| New verdict `Stream to {name} has trouble past the router`, from the stream's own losses and frame recoveries | User said "yes" to building it; the only signal for devices behind the second router |
| The new verdict ranks below adapter, router and saved-device verdicts | Claude's call: those are more specific |
| Trigger levels: 2 or more new losses, or 3 or more new frame recoveries, within 30 s | Claude's call so one hiccup does not trip it; told to the user as adjustable |
| Work with the two-router setup as it is | User cannot re-cable (hard rule 13) |
| Memory test run with the app minimized to the tray | That is how the user runs it |
| No memory budget; about 216 MB is accepted | User, session 3: "i have no idea where the memory budget came from, i never asked for it, but where it is, or +- a few doesn't matter to personal home use." The 150 MB figure was Claude's invention |

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
- Add saved devices `pp wired` / `pp Wi-Fi` by address → not possible; the iPad is behind the ASUS router and cannot be pinged from Stella. Use the "past the router" verdict instead.
- "Which device is at 192.168.2.75 / .76 / .79?" → dropped; the user is "not sure", and the streaming devices are not on that network anyway.
- ❌ Moving Stella's cable to the ASUS router (hard rule 13).
- ❌ Pinging the ASUS router's internet-side address from Stella: tried with "Respond ICMP Echo (ping) Request from WAN" applied; pings stop at 192.168.2.254. Claude advised turning the setting back to No.
- ❌ Scanning the network from Stella to find the iPad (results unreliable and the iPad is on another network).
- ❌ Turning notifications on (hard rule 12).
- ❌ The 150 MB memory budget and any work to reduce memory (user dropped it in session 3; only a leak would matter).

## 8. Data & facts (exact)

**Repository**
| Item | Value |
|---|---|
| Repo | `Don-Keyman/forkin` |
| Branch | `claude/keen-dirac-nr178b` |
| PR | #1, draft, open: https://github.com/Don-Keyman/forkin/pull/1 (description updated in handoff #2; no CI, no reviewers, no comments) |
| Latest docs commit | `a51fbf8` "docs: drop the memory budget; record what changed after building" (session 3; docs only) |
| Latest code commit | `79d43a6` "feat: point at the far side when a stream reports losses behind a healthy router" |
| Tests | 164 passing (`dotnet test tests/StreamNetMon.Core.Tests`) |
| Design doc | `docs/superpowers/specs/2026-11-14-stream-network-monitor-design.md` |
| Plan | `docs/superpowers/plans/2026-11-14-stream-network-monitor.md` |

**Home network (as found in handoff #2)**

`Stella ─wired─▶ provider's box 192.168.2.254 ─▶ ASUS router (its internet side) ─wired or Wi-Fi─▶ iPad and other streaming devices (192.168.71.x)`

| Item | Value |
|---|---|
| Stella | 192.168.2.67, /24, Ethernet only; its Wi-Fi card (Realtek 8922AE) is disconnected |
| Provider's box ("modem (router)") | 192.168.2.254; Stella's gateway; about 1 ms, 0% loss; "acts as pass through to the upstairs router" |
| Second router | ASUS GT-BE98 Pro; the user calls it "upstairs router"; the ASUS app shows Location "First Floor"; firmware 3.0.0.6.102_39260; own network, address 192.168.71.1; WAN type DHCP |
| ASUS internet-side address | Not written here (it is the home's public address). Shown as "WAN IP" in the ASUS app. Not needed: it does not answer from Stella |
| From Stella | 192.168.71.1 times out; the ASUS's internet-side address times out; trace stops after hop 1 (192.168.2.254) |
| Other addresses Stella has seen | 192.168.2.79 (rarely answers; unidentified), 192.168.2.75, 192.168.2.76 (unreachable; unidentified) |

**Stella (the user's PC)**
| Item | Value |
|---|---|
| OS | Windows 11 Pro, 8 logical processors |
| Adapter | "Ethernet", Realtek PCIe 2.5GbE Family Controller, 2.5 Gbps |
| Streaming server | Vibepollo at `C:\Program Files\Apollo` (`sunshine.exe`, service "ApolloService"), web interface `https://localhost:47990` |
| Tools | .NET SDK 10.0.300, Node 24, Python, git 2.56.1 |
| Remote Desktop Commander device | "Stella", ID `112fb53f-9e5e-448a-a2b4-245b72ca0b77` |
| Source clone | `C:\Users\zoso7\forkin`, at `79d43a6` |
| Running app | `C:\Users\zoso7\forkin\publish\StreamNetMon.exe`, built from `79d43a6`, started about 19:11 local on 2026-11-14, minimized to the tray |
| App data | `%LOCALAPPDATA%\StreamNetMon\` (`settings.json`, `history.db`, `diagnostics\`). There is no `log.txt` there |
| Helper scripts in `%TEMP%` | `snm-build.ps1`, `snm-publish.ps1` (stops the app, `git pull --ff-only`, publishes, restarts it), `snm-shot.ps1`, `snm-tab.ps1` (`-Name Live` etc., then screenshots), `snm-click.ps1`, `snm-mem.ps1` (new: one memory reading a minute for 2 hours) |
| Helper outputs in `%TEMP%` | `snm-build.log`, `snm-foot.txt`, `snm-shot.png`, `snm-mem.csv` |
| Local time | UTC−6 |

**Vibepollo token and API (verified against the real server)**
- Token: [secret removed: re-enter it]. Already saved in the app on Stella; sent as `Authorization: Bearer`. It works.
- Scopes (all GET): `/api/session/status`, `/api/rtsp/sessions`, `/api/clients/list`, `/api/host/stats`.
- Unauthenticated: known paths answer 401, unknown paths 404.
- `/api/rtsp/sessions` → `{"sessions":[{device_name, uuid, state, uptime_seconds, codec, width, height, hdr, fps, encoder_bitrate_kbps, requested_bitrate_kbps, client_reported_losses, idr_requests, invalidate_ref_count, bytes_sent, packets_sent, frames_sent, encode_latency_ms, ...}]}`. **No client address.**
- `/api/clients/list` → `{"named_certs":[{name, connected, last_seen, uuid, ...}]}`.
- `/api/host/stats` → `cpu_percent, cpu_temp_c, gpu_percent, gpu_encoder_percent, gpu_temp_c, net_rx_bps, net_tx_bps, ram_percent, ram_total_bytes, ram_used_bytes, vram_*`.
- `/api/session/status` → `{"activeSessions":1,"appName":"Desktop","appRunning":true,"lastEncoderProbeFailed":false,"paused":false,"status":true}`.
- Paired client names: `pp` (the iPad), `stairwire` (first seen in handoff #2; device not identified), `V++`, `Lap`, `mooigjt enhanc`.

**Streams seen on Stella in handoff #2**
| Stream | Picture | Encoder setting | Notes |
|---|---|---|---|
| `pp` | 3840×2160 HEVC HDR, 120 fps | 500.0 Mbps, later 399.0 Mbps | iPad; streamed on Wi-Fi, then wired; 0 client-reported losses throughout |
| `stairwire` | 2560×1440 HEVC, 480 fps | 119.0 Mbps | Second stream; its line went away when it disconnected |

**App constants**
- Degraded defaults: loss ≥ 1%; jitter ≥ 5 ms wired / ≥ 15 ms Wi-Fi; latency ≥ 10 ms wired / ≥ 30 ms Wi-Fi.
- History: SQLite, 5 s buckets, 14-day retention. Server polling: detection 30 s, sessions 2 s, host stats 5 s; request timeout 2 s.
- Verdicts, exactly, in rank order: `Desktop adapter problem`, `Network path to router is degraded`, `Wi-Fi hop to {name} is degraded`, `Wired path to {name} is degraded`, `Stream to {name} has trouble past the router`, `All clear`.
- Stream trouble (`StreamTroubleTracker`): window 30 s; `LossesInWindow` = 2; `RecoveriesInWindow` = 3 (frame recoveries = `idr_requests`); counters going backwards mean a new stream and start over; level Warning.
- Footprint: 1% of one core. No memory budget (dropped by the user in session 3).

**Footprint measurements on Stella**
| When | Conditions | Working set | Private memory | CPU |
|---|---|---|---|---|
| Handoff #1 | Release, window hidden, 11 minutes | 96 → 112 MB | 26 → 38 MB | about 0.24% of one core |
| Handoff #2, 8 s after start | window open | 150 MB | 83 MB | — |
| Handoff #2, 19:15 | window open and maximized | 183 MB | 130 MB | — |
| Handoff #2, 19:20 | minimized to tray shortly before | 214 MB | 158 MB | — |
| Handoff #2, 20:16 | in tray | 216 MB | 160 MB | — |
| Handoff #2, 21:14 (last of 120 readings) | in tray | 216 MB | 159 MB | about 0.5% of one core over 2 hours |

Memory rose for the first 5 minutes, then stayed within 2 MB for 2 hours: no leak. About 216 MB, which the user accepts (the 150 MB budget was dropped in session 3). Handles 812–860, threads 20–23, steady. Cause not confirmed; Claude's guess is display memory kept from the maximized window.

**Lessons that cost time**
- Windows takes 2048 ms to refuse an IPv6 loopback connect; the app uses `LoopbackConnector` (races IPv4/IPv6 loopback, refuses any other host).
- Stella's .NET had no NuGet source; the repo has a `nuget.config` pointing at nuget.org.
- Remote Desktop Commander commands on Stella return after 3 seconds: start builds with `Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',"$env:TEMP\snm-publish.ps1"` and read `%TEMP%\snm-build.log` afterwards (last line `DONE`). The shell is already PowerShell; do not wrap commands in `powershell -Command "..."` (it strips `$`).
- Running a script directly (`& "$env:TEMP\x.ps1"`) fails: scripts are disabled. Use `powershell -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\snm-tab.ps1" -Name Live`, then read `C:\Users\zoso7\AppData\Local\Temp\snm-shot.png`.
- Remote Desktop Commander blocks `netsh` (and `net`, `sc`, `reg`). Use `Get-NetAdapter`, `Get-NetNeighbor`, `Get-NetIPConfiguration`, `tracert`.
- `git pull` writes progress to stderr, so the build log shows a red "NativeCommandError" line that is not a failure.
- The cloud container has no .NET by default: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /root/.dotnet`, then `export PATH=/root/.dotnet:$PATH`. Build the app there with `-p:EnableWindowsTargeting=true`.
- A new cloud session checks out its own branch with only the initial commit; `git fetch origin` and check out `claude/keen-dirac-nr178b`.
- GitHub work in a cloud session goes through the `mcp__github__*` tools (load with ToolSearch); the "plugin:github" server fails to connect and is not needed.
- In Claude Code on Stella itself (working directory `C:\Users\zoso7\forkin`), no connector is needed: run commands locally instead of through Remote Desktop Commander, and use `gh`, which is signed in as Don-Keyman. Check which kind of session this is before reaching for Remote Desktop Commander.
- Do not add the GitHub MCP server on Stella: `claude mcp add --transport http github https://api.githubcopilot.com/mcp/` fails with "Incompatible auth server: does not support dynamic client registration". It was removed; the user agreed `gh` is enough.
- Commits from Stella use the repo-only identity `Don-Keyman` with GitHub's no-reply address (already set in this clone; the user chose it over their real email).

## 9. People, terms & names
- **People:** the user (owner of Stella and the repo).
- **Terms & nicknames:** Stella = the Windows streaming host. Vibepollo = the streaming server in use (Sunshine/Apollo fork). "pp" = the iPad's paired name. "stairwire" = another paired client. "signal" = the user's loose word for connection quality. "modem (router)" / provider's box = 192.168.2.254. "upstairs router" = the ASUS GT-BE98 Pro. "frame recoveries" = the app's label for `idr_requests`. "far side" / "past the router" = everything beyond 192.168.2.254.
- **Names in use:** solution `StreamNetMon.sln`; projects `src/StreamNetMon.Core`, `src/StreamNetMon.App`, `tests/StreamNetMon.Core.Tests`; exe `StreamNetMon.exe`; window title "Stream network monitor"; tabs Live / History / Devices / Settings; key types `MonitorEngine`, `StreamServerLink`, `VibepolloParser`, `VerdictEngine`, `StreamTroubleTracker` (in `src/StreamNetMon.Core/StreamTrouble.cs`), `HistoryStore`, `RollingWindow`, `AdapterTracker`, `LoopbackConnector`, `AlertGate`; tests `StreamTroubleTests.cs`, `VerdictTests.cs`, `MonitorEngineTests.cs`.

## 10. Work state
| Item | Status | Version / location | Notes |
|---|---|---|---|
| Design doc | final, approved | `docs/superpowers/specs/…-design.md` | Predates "no client address", the two-router finding and the new verdict |
| Implementation plan | final, approved | `docs/superpowers/plans/…md` | Tasks 1–12 all worked |
| Core library + tests | done | commit `79d43a6`, 164 tests | |
| WPF app (4 tabs, tray, notifications) | done | commit `79d43a6` | Builds with 0 warnings |
| "Past the router" verdict | built, tested, deployed | `StreamTrouble.cs`, `Verdict.cs`, `MonitorEngine.cs`, README | Not yet seen firing on Stella; needs a stream that is really struggling |
| Vibepollo link | working with real token | on Stella | Two streams showed as two ▶ lines; a line goes when its stream ends |
| App on Stella | running, in tray | built from `79d43a6` | Shows "All clear"; "encoder set to" label confirmed |
| Saved devices on Stella | none; only the gateway | — | Streaming devices cannot be pinged (section 7) |
| Memory | accepted, not leaking | `%TEMP%\snm-mem.csv` (finished, 120 readings) | About 216 MB, flat; user dropped the budget in session 3; no work needed |
| Design doc | updated in session 3 | `docs/superpowers/specs/…-design.md` | New end section "What changed after building": two networks, no client address, new verdict, no memory limit |
| ASUS "Respond ICMP Echo (ping) Request from WAN" | user applied it; did not help | ASUS app | User turned it back off (confirmed in session 3) |
| README | updated | repo root | Lists the new verdict |
| PR #1 | draft, open | description current (164 tests) | No CI in the repo |
| Sunshine mode | untested | — | No Sunshine install |
| Device offline/online on real hardware | untested | — | Covered by unit tests only |
| Final review | self-review only | — | No independent reviewer ran |

Deferred minors: the first adapter reading after start records a 0 send rate (a dip in history); "Seen on your network" lists the gateway and shows blank names (router has no reverse DNS).

## 11. Next steps
1. **Next action:** See the "Stream to pp has trouble past the router" verdict fire on Stella during a real bad stream; adjust the trigger levels (2 losses / 3 recoveries in 30 s) if it is too jumpy or too quiet. This waits on the user noticing a bad stream.
3. Still untested: Sunshine mode; a device going offline and back on real hardware.
4. When testing is finished, ask whether to delete the helper scripts and outputs in Stella's `%TEMP%`.
5. PR #1: the old session's subscription and check-ins do not carry over; subscribe again if the user wants it watched.

Done in session 3: memory question closed (no budget); design doc updated.

## 12. Open questions ⚠️
- Which device is `stairwire`? (Only matters for naming; Claude guessed a wired spot by the stairs, unconfirmed.)
- Should the helper scripts in Stella's `%TEMP%` be deleted when testing is finished?

## 13. Re-attach checklist
Nothing to attach. Everything is in the repository `Don-Keyman/forkin` on branch `claude/keen-dirac-nr178b`; this file is at the repo root as `handoff-stream-network-monitor.md`.

---
<sub>Audit: 13/13 sections · 14 rules · 9 corrections · 90+ data points · secrets removed: yes · generated by active-memory</sub>
