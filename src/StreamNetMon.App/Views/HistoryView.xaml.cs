using System.Windows;
using StreamNetMon.App.Controls;

namespace StreamNetMon.App.Views;

public partial class HistoryView : System.Windows.Controls.UserControl
{
    private const int MaxPoints = 600;
    private static readonly TimeSpan[] Ranges = [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromDays(7)];

    private Services? _services;
    private DateTimeOffset? _end;   // null = follow the present
    private int _request;
    private bool _loading;

    public HistoryView() => InitializeComponent();

    public void Attach(Services services)
    {
        _services = services;
        IsVisibleChanged += (_, _) => { if (IsVisible) ReloadDevices(); };
    }

    private void ReloadDevices()
    {
        if (_services is null) return;
        _loading = true;
        var selected = (DeviceBox.SelectedItem as Device)?.Id;
        var saved = _services.Settings.Devices.Select(d => d.Id).ToHashSet();
        var devices = _services.Settings.Devices
            .Concat(_services.Engine.Current.Targets.Select(t => t.Device).Where(d => !saved.Contains(d.Id)))
            .ToList();
        DeviceBox.ItemsSource = devices;
        DeviceBox.SelectedItem = devices.FirstOrDefault(d => d.Id == selected) ?? devices.FirstOrDefault();
        _loading = false;
        Refresh();
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsLoaded) Refresh();
    }

    private TimeSpan Range => Ranges[Math.Clamp(RangeBox.SelectedIndex, 0, Ranges.Length - 1)];

    private void Earlier_Click(object sender, RoutedEventArgs e)
    {
        _end = (_end ?? DateTimeOffset.UtcNow) - Range / 2;
        Refresh();
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_end is not { } end) return;
        _end = end + Range / 2 >= DateTimeOffset.UtcNow ? null : end + Range / 2;
        Refresh();
    }

    private void Now_Click(object sender, RoutedEventArgs e)
    {
        _end = null;
        Refresh();
    }

    private async void Refresh()
    {
        if (_services is null) return;
        var history = _services.History;
        var device = DeviceBox.SelectedItem as Device;
        var to = _end ?? DateTimeOffset.UtcNow;
        var from = to - Range;
        var request = ++_request;
        LaterButton.IsEnabled = _end is not null;
        RangeText.Text = $"{from.ToLocalTime():ddd d MMM HH:mm} – {to.ToLocalTime():ddd d MMM HH:mm}";

        var data = await Task.Run(() =>
        {
            var target = device is null ? [] : history.QueryTarget(device.Id, from, to);
            var adapter = history.QueryAdapter(from, to);
            var sessions = history.QuerySessions(from, to);
            return (
                Latency: Line(target.Select(b => (b.Start, b.Offline ? null : b.MaxMs)), from, to),
                Jitter: Line(target.Select(b => (b.Start, b.Offline ? null : b.JitterMs)), from, to),
                Loss: Line(target.Select(b => (b.Start, b.Offline ? (double?)null : b.LossPct)), from, to),
                Send: Line(adapter.Select(b => (b.Start, (double?)b.SendBps)), from, to),
                Bands: (IReadOnlyList<ChartBand>)sessions.Select(s => new ChartBand(s.Start, s.End ?? to, s.ClientName)).ToList());
        });
        if (request != _request) return;   // a newer request has replaced this one

        foreach (var (chart, series) in new[] { (LatencyChart, data.Latency), (JitterChart, data.Jitter), (LossChart, data.Loss), (SendChart, data.Send) })
        {
            chart.From = from;
            chart.To = to;
            chart.Bands = data.Bands;
            chart.Series = [series];
        }
    }

    /// <summary>Turns stored 5-second buckets into a drawable line, with gaps where no data was recorded.</summary>
    private static ChartSeries Line(IEnumerable<(DateTimeOffset At, double? Value)> buckets, DateTimeOffset from, DateTimeOffset to)
    {
        var points = new List<(DateTimeOffset At, double? Value)>();
        DateTimeOffset? previous = null;
        foreach (var b in buckets)
        {
            // More than 15 s without a bucket means the app was not running: break the line.
            if (previous is { } p && b.At - p > TimeSpan.FromSeconds(15)) points.Add((p.AddSeconds(5), null));
            points.Add(b);
            previous = b.At;
        }
        var reduced = Downsample.Max(points, from, to, MaxPoints);
        return new ChartSeries(Palette.Accent, reduced.Select(x => new ChartPoint(x.At, x.Value)).ToList());
    }
}
