# Stream network monitor

A Windows tray app that runs beside a Sunshine-family game-streaming server
(Vibepollo, Apollo, Sunshine) and shows the quality of the connection from the
host PC to the devices it streams to inside the house.

## Build and test

Requires the .NET 10 SDK.

```sh
dotnet test                                   # core logic tests (any OS)
dotnet build src/StreamNetMon.App             # the app (Windows)
dotnet run --project src/StreamNetMon.App     # run it (Windows)
```

On Linux/macOS the app compiles with `-p:EnableWindowsTargeting=true` but only
runs on Windows.

Design: `docs/superpowers/specs/2026-11-14-stream-network-monitor-design.md`
