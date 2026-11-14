using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamNetMon.Core;

public enum ServerMode { Auto, Vibepollo, Sunshine, None }

public sealed class AppSettings
{
    public const int DefaultServerPort = 47990;

    public Thresholds Thresholds { get; set; } = new();
    public bool StartWithWindows { get; set; }
    public bool Notifications { get; set; }
    public ServerMode ServerMode { get; set; } = ServerMode.Auto;
    public int ServerPort { get; set; } = DefaultServerPort;

    /// <summary>The streaming server's access token, already protected by an <see cref="ISecretProtector"/>.</summary>
    public string? ProtectedToken { get; set; }

    public List<Device> Devices { get; set; } = [];
}

/// <summary>Loads and saves <see cref="AppSettings"/> as settings.json in a directory.</summary>
public sealed class SettingsStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; } = Path.Combine(directory, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();
        try
        {
            var text = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(text)) return new AppSettings();
            return Normalise(JsonSerializer.Deserialize<AppSettings>(text, Json));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            // Keep what was there so nothing is silently lost, then start clean.
            File.Move(FilePath, FilePath + ".bad", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
        File.Move(temp, FilePath, overwrite: true);
    }

    private static AppSettings Normalise(AppSettings? s)
    {
        s ??= new AppSettings();
        s.Thresholds ??= new Thresholds();
        s.Devices ??= [];
        s.Devices.RemoveAll(d => d is null || string.IsNullOrWhiteSpace(d.Host));
        if (s.ServerPort is < 1 or > 65535) s.ServerPort = AppSettings.DefaultServerPort;
        return s;
    }
}
