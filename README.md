# Stream network monitor

A Windows tray app that runs beside a Sunshine-family game-streaming server
(Vibepollo, Apollo, Sunshine) and shows the quality of the connection from the
host PC to the devices it streams to inside the house.

It answers one question: when a stream stutters, is it the PC, the wired run,
the router, or the Wi-Fi hop?

## What it shows

- **Verdict** at the top, one of:
  - `All clear`
  - `Desktop adapter problem` — the PC's link speed has dropped or it is logging send errors
  - `Network path to router is degraded` — the gateway or a saved router is slow or losing packets
  - `Wi-Fi hop to <device> is degraded` — the routers are fine, a Wi-Fi device is not
  - `Wired path to <device> is degraded`
  - `Stream to <device> has trouble past the router` — this PC and its router are
    fine, but the streaming device itself reports lost data (2 or more in 30 s) or
    keeps asking for frame recoveries (3 or more in 30 s). This is how trouble shows
    when the device sits behind a second router that this PC cannot ping through.
- **Per device**: latency, jitter and packet loss over the last 30 seconds, and
  a bar per second for the last minute (a red bar is a lost probe).
- **The PC**: adapter name, link speed, send errors, discards, and how much it
  is sending.
- **History**: 14 days of latency, jitter, loss and send rate, with each
  streaming session drawn as a shaded band.

The tray icon is green, amber or red to match the verdict. Closing the window
keeps the app running in the tray; use the tray menu to exit.

## Devices

The default gateway is watched automatically. Add everything else on the
**Devices** tab: pick an address from "Seen on your network", give it a name,
and say whether it is wired or on Wi-Fi.

A device that connects both by cable and by Wi-Fi has a different address for
each. Add it twice ("iPad wired", "iPad Wi-Fi") to compare them. Add the
upstairs router or access point as kind "Router" so a bad Wi-Fi hop can be told
apart from a bad path to the router.

A device that has not answered for 60 seconds is shown as offline and raises no
alarm (tablets sleep).

## Streaming server link

The app looks for the streaming server's web interface on
`https://localhost:47990` and adapts to what it finds:

| Server | What the app gets |
|---|---|
| Vibepollo / Apollo | who is streaming right now, each stream's resolution, frame rate, bitrate, client-reported losses and frame recoveries, host statistics, session start and end |
| Sunshine | that it is running; paired device names where the server gives them |
| none | nothing; saved devices only |

Detection is automatic; **Settings → Streaming server** can force a mode or
change the port. Network measurement never depends on the server: if it is
stopped, replaced or answers something unexpected, monitoring carries on.

Vibepollo reports the streaming client by its paired name, not its network
address. To tie a stream to a row, give the saved device the same name as the
paired client, optionally followed by a label: for a client paired as `pp`, save
`pp wired` and `pp Wi-Fi`. The one that is online while `pp` streams is marked
with ▶, and the session is recorded against it in History.

For live sessions on Vibepollo/Apollo, create an access token in the
server's web page with GET access to `/api/session/status`, `/api/rtsp/sessions`,
`/api/clients/list` and `/api/host/stats`, and paste it into **Settings → Access token**. It is stored
encrypted for your Windows account (DPAPI) and only ever sent to this PC's own
streaming server. The app only reads; it never changes the server.

## Limits

- The PC cannot see Wi-Fi signal strength on the client devices, only its
  effect: latency, jitter and loss.
- Throughput is for the whole network adapter, not just the stream.
- Latency is measured with ping from a normal (non-administrator) program, so
  sub-millisecond figures include a little scheduling noise.
- Sunshine mode is reduced: stock Sunshine does not report live sessions.

## Where data lives

`%LOCALAPPDATA%\StreamNetMon\` — `settings.json`, `history.db`, `log.txt`, and
`diagnostics\` if you use "Save server responses for diagnostics".

## Build, test, run

Requires the .NET 10 SDK.

```sh
dotnet test                                   # core logic tests (any OS)
dotnet build src/StreamNetMon.App             # the app (Windows)
dotnet run --project src/StreamNetMon.App     # run it (Windows)
```

To make a folder you can run from anywhere:

```sh
dotnet publish src/StreamNetMon.App -c Release -r win-x64 --self-contained false -o publish
publish\StreamNetMon.exe
```

On Linux/macOS the app compiles with `-p:EnableWindowsTargeting=true` but only
runs on Windows.

## Layout

```
src/StreamNetMon.Core/        measurement, verdict, history, server link (no UI)
src/StreamNetMon.App/         WPF window, tray icon, Windows-specific sources
tests/StreamNetMon.Core.Tests/
docs/superpowers/             design and implementation plan
```
