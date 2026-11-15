using System.Collections.ObjectModel;
using System.Windows.Media;
using StreamNetMon.App.Controls;

namespace StreamNetMon.App.ViewModels;

public sealed class TargetRow : Observable
{
    private string _name = "", _type = "", _latency = "", _jitter = "", _loss = "";
    private Brush _dot = Palette.Idle;
    private bool _offline, _isSession;
    private double _ceiling = 10;
    private IReadOnlyList<ProbeResult> _samples = [];

    public Guid Id { get; init; }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Type { get => _type; set => Set(ref _type, value); }
    public string Latency { get => _latency; set => Set(ref _latency, value); }
    public string Jitter { get => _jitter; set => Set(ref _jitter, value); }
    public string Loss { get => _loss; set => Set(ref _loss, value); }
    public Brush Dot { get => _dot; set => Set(ref _dot, value); }
    public bool Offline { get => _offline; set => Set(ref _offline, value); }
    public bool IsSession { get => _isSession; set => Set(ref _isSession, value); }
    public double Ceiling { get => _ceiling; set => Set(ref _ceiling, value); }
    public IReadOnlyList<ProbeResult> Samples { get => _samples; set => Set(ref _samples, value); }
}

public sealed class LiveViewModel : Observable
{
    private static readonly TimeSpan ChartSpan = TimeSpan.FromMinutes(10);

    private readonly Queue<ChartPoint> _throughput = new();
    private string _verdict = "Starting…", _server = "", _sendRate = "", _adapter = "", _hostStats = "", _streams = "";
    private Brush _verdictBrush = Palette.Idle;
    private IReadOnlyList<ChartSeries> _series = [];
    private DateTimeOffset _from, _to;

    public ObservableCollection<TargetRow> Targets { get; } = [];
    public string Verdict { get => _verdict; private set => Set(ref _verdict, value); }
    public Brush VerdictBrush { get => _verdictBrush; private set => Set(ref _verdictBrush, value); }
    public string Server { get => _server; private set => Set(ref _server, value); }
    public string SendRate { get => _sendRate; private set => Set(ref _sendRate, value); }
    public string Adapter { get => _adapter; private set => Set(ref _adapter, value); }
    public string HostStats { get => _hostStats; private set => Set(ref _hostStats, value); }
    public string Streams { get => _streams; private set => Set(ref _streams, value); }
    public IReadOnlyList<ChartSeries> Series { get => _series; private set => Set(ref _series, value); }
    public DateTimeOffset From { get => _from; private set => Set(ref _from, value); }
    public DateTimeOffset To { get => _to; private set => Set(ref _to, value); }

    /// <summary>Cheap bookkeeping that must happen every second, shown or not.</summary>
    public void Collect(Snapshot s)
    {
        _throughput.Enqueue(new ChartPoint(s.At, s.Adapter?.SendBps));
        while (_throughput.Count > 0 && s.At - _throughput.Peek().At > ChartSpan) _throughput.Dequeue();
    }

    /// <summary>Refreshes everything on screen. Only called while the window is visible.</summary>
    public void Show(Snapshot s, Thresholds thresholds)
    {
        Verdict = s.Verdict.Message;
        VerdictBrush = Palette.For(s.Verdict.Level);
        Server = DescribeServer(s.Server);
        SendRate = s.Adapter is null ? "" : "↑ " + Format.Rate(s.Adapter.SendBps);
        Adapter = s.Adapter is { } a
            ? $"Desktop: {a.Name} {Format.LinkSpeed(a.LinkSpeedBps)}"
              + (a.LinkSpeedBps < a.UsualLinkSpeedBps ? $" (usually {Format.LinkSpeed(a.UsualLinkSpeedBps)})" : "")
              + $" · send errors {a.OutErrorsDelta} · discards {a.OutDiscardsDelta}"
            : "Desktop: no active network adapter";
        HostStats = Format.HostStats(s.Server.HostStats);
        Streams = string.Join("\n", s.Server.Sessions.Where(x => x.Stream is not null).Select(x =>
            $"▶ {x.ClientName}: {Format.Stream(x.Stream!)} · client-reported losses {x.Stream!.ClientReportedLosses}"
            + $" · frame recoveries {x.Stream.InvalidatedFrames + x.Stream.IdrRequests}"));

        for (var i = Targets.Count - 1; i >= 0; i--)
            if (s.Targets.All(t => t.Device.Id != Targets[i].Id)) Targets.RemoveAt(i);

        for (var i = 0; i < s.Targets.Count; i++)
        {
            var t = s.Targets[i];
            var row = Targets.FirstOrDefault(r => r.Id == t.Device.Id);
            if (row is null)
            {
                row = new TargetRow { Id = t.Device.Id };
                Targets.Insert(Math.Min(i, Targets.Count), row);
            }
            else if (Targets.IndexOf(row) != i && i < Targets.Count)
            {
                Targets.Move(Targets.IndexOf(row), i);
            }

            var wifi = t.Device.Type == ConnectionType.WiFi;
            row.Name = t.Device.Name;
            row.Type = t.Device.Role == TargetRole.StreamingDevice ? (wifi ? "Wi-Fi" : "wired") : "router";
            if (s.StreamingIds.Contains(t.Device.Id)) row.Type += " ▶";
            row.Offline = t.Stats.Offline;
            row.IsSession = s.SessionDeviceIds.Contains(t.Device.Id);
            row.Latency = t.Stats.Offline ? "offline" : Format.Ms(t.Stats.LatencyMs);
            row.Jitter = t.Stats.Offline ? "" : Format.Ms(t.Stats.JitterMs);
            row.Loss = t.Stats.Offline ? "" : Format.Pct(t.Stats.LossPct);
            row.Dot = t.Stats.Offline ? (t.Device.Role != TargetRole.StreamingDevice && VerdictEngine.WentSilent(t) ? Palette.Problem : Palette.Idle)
                : VerdictEngine.IsDegraded(t, thresholds) ? (t.Device.Role == TargetRole.StreamingDevice ? Palette.Warning : Palette.Problem)
                : Palette.Ok;
            row.Ceiling = wifi ? thresholds.LatencyWiFiMs : thresholds.LatencyWiredMs;
            row.Samples = t.Last60s;
        }

        Series = [new ChartSeries(Palette.Accent, _throughput.ToList())];
        From = s.At - ChartSpan;
        To = s.At;
    }

    private static string DescribeServer(ServerStatus server)
    {
        var name = server.Kind switch
        {
            ServerKind.Vibepollo => "Vibepollo",
            ServerKind.Sunshine => "Sunshine",
            _ => "No streaming server found",
        };
        if (server.Sessions.Count > 0)
            name += " · streaming to " + string.Join(", ", server.Sessions.Select(x => x.ClientName).Distinct());
        else if (server.Kind != ServerKind.None && server.Note is null)
            name += " · idle";
        return server.Note is null ? name : $"{name} · {server.Note}";
    }
}
