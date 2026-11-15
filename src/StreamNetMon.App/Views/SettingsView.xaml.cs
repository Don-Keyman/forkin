using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using StreamNetMon.App.Windows;

namespace StreamNetMon.App.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    private Services? _services;

    public SettingsView() => InitializeComponent();

    public void Attach(Services services)
    {
        _services = services;
        IsVisibleChanged += (_, _) => { if (IsVisible) Load(); };
    }

    /// <summary>Called once a second while this tab is showing.</summary>
    public void Show(Snapshot snapshot)
    {
        var kind = snapshot.Server.Kind switch
        {
            ServerKind.Vibepollo => "Found: Vibepollo / Apollo",
            ServerKind.Sunshine => "Found: Sunshine",
            _ => "No streaming server found on this PC",
        };
        Detected.Text = snapshot.Server.Note is { } note ? $"{kind}. {note}." : kind + ".";
    }

    private void Load()
    {
        if (_services is null) return;
        var s = _services.Settings;
        LatencyWired.Text = Text(s.Thresholds.LatencyWiredMs);
        LatencyWifi.Text = Text(s.Thresholds.LatencyWiFiMs);
        JitterWired.Text = Text(s.Thresholds.JitterWiredMs);
        JitterWifi.Text = Text(s.Thresholds.JitterWiFiMs);
        Loss.Text = Text(s.Thresholds.LossPct);
        StartWithWindows.IsChecked = s.StartWithWindows;
        Notifications.IsChecked = s.Notifications;
        Mode.SelectedIndex = (int)s.ServerMode;
        Port.Text = s.ServerPort.ToString(CultureInfo.InvariantCulture);
        SaveMessage.Text = "";
        DataFolder.Text = "Settings and history are kept in " + _services.DataDirectory;
        ShowTokenStatus();
        Show(_services.Engine.Current);
    }

    private void ShowTokenStatus() =>
        TokenStatus.Text = _services?.TokenHint is { } hint ? $"Saved (ends in {hint.TrimStart('…')})" : "No token saved";

    private static string Text(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

    private static bool Number(TextBox box, double min, double max, out double value)
    {
        var ok = (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                  || double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                 && value >= min && value <= max;
        box.BorderBrush = ok ? SystemColors.ControlDarkBrush : Controls.Palette.Problem;
        return ok;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        var ok = Number(LatencyWired, 0.1, 10_000, out var latencyWired)
                 & Number(LatencyWifi, 0.1, 10_000, out var latencyWifi)
                 & Number(JitterWired, 0.1, 10_000, out var jitterWired)
                 & Number(JitterWifi, 0.1, 10_000, out var jitterWifi)
                 & Number(Loss, 0.1, 100, out var loss)
                 & Number(Port, 1, 65535, out var port);
        if (!ok || port != Math.Floor(port))
        {
            SaveMessage.Foreground = Controls.Palette.Problem;
            SaveMessage.Text = "Check the highlighted values.";
            return;
        }

        var startWithWindows = StartWithWindows.IsChecked == true;
        _services.Engine.Edit(s =>
        {
            s.Thresholds = new Thresholds(loss, jitterWired, jitterWifi, latencyWired, latencyWifi);
            s.StartWithWindows = startWithWindows;
            s.Notifications = Notifications.IsChecked == true;
            s.ServerMode = (ServerMode)Math.Max(0, Mode.SelectedIndex);
            s.ServerPort = (int)port;
        });
        _services.SettingsStore.Save(_services.Settings);
        try
        {
            StartupRegistration.Apply(startWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            SaveMessage.Foreground = Controls.Palette.Problem;
            SaveMessage.Text = "Saved, but Windows did not allow the start-up setting to change.";
            return;
        }
        SaveMessage.Foreground = Controls.Palette.Ok;
        SaveMessage.Text = "Saved.";
    }

    private void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        if (_services is null || string.IsNullOrWhiteSpace(Token.Password)) return;
        _services.SetToken(Token.Password);
        Token.Clear();
        ShowTokenStatus();
    }

    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        _services.SetToken(null);
        Token.Clear();
        ShowTokenStatus();
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        var bodies = _services.Server.LastBodies;
        if (bodies.Count == 0)
        {
            DiagnosticsStatus.Text = "Nothing to save yet: the streaming server has not answered.";
            return;
        }
        try
        {
            var folder = Path.Combine(_services.DataDirectory, "diagnostics");
            Directory.CreateDirectory(folder);
            foreach (var (path, body) in bodies)
                File.WriteAllText(Path.Combine(folder, path.Trim('/').Replace('/', '-') + ".json"), body);
            DiagnosticsStatus.Text = $"Saved {bodies.Count} file(s) to {folder}. They hold what the server answered, never your token.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticsStatus.Text = "Could not save: " + ex.Message;
        }
    }
}
