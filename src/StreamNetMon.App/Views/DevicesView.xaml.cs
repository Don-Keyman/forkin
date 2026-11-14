using System.Net;
using System.Windows;
using System.Windows.Controls;

namespace StreamNetMon.App.Views;

public partial class DevicesView : System.Windows.Controls.UserControl
{
    public sealed record SavedItem(Guid Id, string Name, string Host, string Kind);
    public sealed record FoundItem(string Address, string Name);

    private readonly Dictionary<string, string> _names = new();
    private Services? _services;
    private Guid? _editing;

    public DevicesView() => InitializeComponent();

    public void Attach(Services services)
    {
        _services = services;
        IsVisibleChanged += (_, _) => { if (IsVisible) Reload(); };
    }

    /// <summary>Called once a second while this tab is showing.</summary>
    public void Show(Snapshot snapshot)
    {
        var streaming = snapshot.Targets
            .Where(t => snapshot.SessionDeviceIds.Contains(t.Device.Id))
            .Select(t => new SavedItem(t.Device.Id, t.Device.Name, t.Device.Host, ""))
            .ToList();
        if (!streaming.SequenceEqual(Streaming.Items.OfType<SavedItem>())) Streaming.ItemsSource = streaming;
        StreamingTitle.Visibility = Streaming.Visibility = streaming.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var paired = snapshot.Server.PairedNames;
        if (!paired.SequenceEqual(NameBox.Items.OfType<string>()))
        {
            var text = NameBox.Text;
            NameBox.ItemsSource = paired.ToList();
            NameBox.Text = text;
        }
    }

    private void Reload()
    {
        if (_services is null) return;
        Saved.ItemsSource = _services.Settings.Devices
            .Select(d => new SavedItem(d.Id, d.Name, d.Host, Describe(d)))
            .ToList();
        RefreshFound();
        Show(_services.Engine.Current);
    }

    private static string Describe(Device d) =>
        d.Role == TargetRole.StreamingDevice ? (d.Type == ConnectionType.WiFi ? "Wi-Fi" : "wired") : "router";

    private async void RefreshFound()
    {
        if (_services is null) return;
        var services = _services;
        var saved = services.Settings.Devices.Select(d => d.Host).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addresses = await Task.Run(() => services.Network.Neighbours().Select(n => n.Address).ToList());
        addresses = addresses.Where(a => !saved.Contains(a)).OrderBy(a => IPAddress.TryParse(a, out var ip) ? BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray()) : 0).ToList();
        Found.ItemsSource = addresses.Select(a => new FoundItem(a, _names.GetValueOrDefault(a, ""))).ToList();

        // Names come from reverse DNS, which can be slow or absent: fill them in afterwards.
        var missing = addresses.Where(a => !_names.ContainsKey(a)).ToList();
        if (missing.Count == 0) return;
        var resolved = await Task.WhenAll(missing.Select(async a =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                return (a, (await Dns.GetHostEntryAsync(a, cts.Token)).HostName);
            }
            catch (Exception)
            {
                return (a, "");
            }
        }));
        foreach (var (address, name) in resolved) _names[address] = name;
        Found.ItemsSource = addresses.Select(a => new FoundItem(a, _names.GetValueOrDefault(a, ""))).ToList();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshFound();

    private void Found_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Found.SelectedItem is not FoundItem item) return;
        StartNew();
        HostBox.Text = item.Address;
        NameBox.Text = item.Name.Split('.')[0];
        NameBox.Focus();
    }

    private void Saved_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Saved.SelectedItem is not SavedItem item || _services is null) return;
        var device = _services.Settings.Devices.FirstOrDefault(d => d.Id == item.Id);
        if (device is null) return;
        _editing = device.Id;
        FormTitle.Text = "Edit device";
        SaveButton.Content = "Save";
        RemoveButton.IsEnabled = true;
        NameBox.Text = device.Name;
        HostBox.Text = device.Host;
        TypeBox.SelectedIndex = device.Type == ConnectionType.WiFi ? 1 : 0;
        RoleBox.SelectedIndex = device.Role == TargetRole.StreamingDevice ? 0 : 1;
        Message.Text = "";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        Saved.SelectedItem = null;
        StartNew();
        NameBox.Text = HostBox.Text = "";
    }

    private void StartNew()
    {
        _editing = null;
        FormTitle.Text = "Add a device";
        SaveButton.Content = "Add";
        RemoveButton.IsEnabled = false;
        Message.Text = "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_services is null) return;
        var name = NameBox.Text.Trim();
        var host = HostBox.Text.Trim();
        if (name.Length == 0 || host.Length == 0)
        {
            Message.Text = "Give the device a name and an address.";
            return;
        }
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            Message.Text = "That address does not look like an IP address or a host name.";
            return;
        }
        if (_services.Settings.Devices.Any(d => d.Id != _editing && string.Equals(d.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            Message.Text = "Another saved device already uses that address.";
            return;
        }

        var device = new Device(_editing ?? Guid.NewGuid(), name, host,
            TypeBox.SelectedIndex == 1 ? ConnectionType.WiFi : ConnectionType.Wired,
            RoleBox.SelectedIndex == 1 ? TargetRole.Router : TargetRole.StreamingDevice);
        _services.Engine.Edit(s =>
        {
            var index = s.Devices.FindIndex(d => d.Id == device.Id);
            if (index >= 0) s.Devices[index] = device;
            else s.Devices.Add(device);
        });
        _services.SettingsStore.Save(_services.Settings);
        New_Click(sender, e);
        Reload();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_services is null || _editing is not { } id) return;
        _services.Engine.Edit(s => s.Devices.RemoveAll(d => d.Id == id));
        _services.SettingsStore.Save(_services.Settings);
        New_Click(sender, e);
        Reload();
    }

    private void KeepWired_Click(object sender, RoutedEventArgs e) => Keep(sender, ConnectionType.Wired);

    private void KeepWifi_Click(object sender, RoutedEventArgs e) => Keep(sender, ConnectionType.WiFi);

    private void Keep(object sender, ConnectionType type)
    {
        if (_services is null || sender is not Button { Tag: Guid id }) return;
        try
        {
            _services.Engine.PinSessionDevice(id, type);
            _services.SettingsStore.Save(_services.Settings);
        }
        catch (InvalidOperationException ex)
        {
            Message.Text = ex.Message;
        }
        Reload();
    }
}
