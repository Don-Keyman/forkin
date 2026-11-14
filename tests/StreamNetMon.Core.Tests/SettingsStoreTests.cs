namespace StreamNetMon.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snm-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.Equal(47990, s.ServerPort);
        Assert.False(s.Notifications);
        Assert.False(s.StartWithWindows);
        Assert.Equal(ServerMode.Auto, s.ServerMode);
        Assert.Equal(new Thresholds(), s.Thresholds);
        Assert.Empty(s.Devices);
        Assert.Null(s.ProtectedToken);
    }

    [Fact]
    public void Round_trip_preserves_devices_and_thresholds()
    {
        var store = new SettingsStore(_dir);
        var device = new Device(Guid.NewGuid(), "iPad Wi-Fi", "192.168.1.40", ConnectionType.WiFi, TargetRole.StreamingDevice);
        var s = new AppSettings
        {
            Thresholds = new Thresholds(LossPct: 2, JitterWiFiMs: 20),
            Notifications = true,
            ServerMode = ServerMode.Sunshine,
            ServerPort = 47991,
            ProtectedToken = "abc",
            Devices = { device },
        };
        store.Save(s);
        var back = new SettingsStore(_dir).Load();
        Assert.Equal(s.Thresholds, back.Thresholds);
        Assert.True(back.Notifications);
        Assert.Equal(ServerMode.Sunshine, back.ServerMode);
        Assert.Equal(47991, back.ServerPort);
        Assert.Equal("abc", back.ProtectedToken);
        Assert.Equal(device, Assert.Single(back.Devices));
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_keeps_bad_copy()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "{\"Devices\": [");
        var s = new SettingsStore(_dir).Load();
        Assert.Equal(47990, s.ServerPort);
        Assert.Equal("{\"Devices\": [", File.ReadAllText(File_ + ".bad"));
        Assert.False(File.Exists(File_));
    }

    [Fact]
    public void Empty_file_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "");
        Assert.Empty(new SettingsStore(_dir).Load().Devices);
    }

    [Fact]
    public void Json_null_gives_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "null");
        Assert.Equal(47990, new SettingsStore(_dir).Load().ServerPort);
    }

    [Fact]
    public void Null_members_fall_back_to_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "{\"Devices\": null, \"Thresholds\": null, \"ServerPort\": 0}");
        var s = new SettingsStore(_dir).Load();
        Assert.Empty(s.Devices);
        Assert.Equal(new Thresholds(), s.Thresholds);
        Assert.Equal(47990, s.ServerPort);
    }

    [Fact]
    public void Unknown_properties_are_ignored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, "{\"FutureThing\": 5, \"ServerPort\": 1234}");
        Assert.Equal(1234, new SettingsStore(_dir).Load().ServerPort);
    }

    [Fact]
    public void Save_creates_directory()
    {
        new SettingsStore(Path.Combine(_dir, "nested")).Save(new AppSettings());
        Assert.True(File.Exists(Path.Combine(_dir, "nested", "settings.json")));
    }
}
