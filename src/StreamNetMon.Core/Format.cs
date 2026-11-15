using System.Globalization;

namespace StreamNetMon.Core;

/// <summary>Text for the numbers the app shows. Always uses a dot as the decimal separator.</summary>
public static class Format
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly (double Size, string Unit)[] Units = [(1e3, "Kbps"), (1e6, "Mbps"), (1e9, "Gbps")];

    public static string Rate(double bps)
    {
        if (bps <= 0 || double.IsNaN(bps)) return "0 Kbps";
        foreach (var (size, unit) in Units)
        {
            var value = Math.Round(bps / size, 1);
            if (value < 1000 || unit == "Gbps") return value.ToString("0.0", Inv) + " " + unit;
        }
        return "0 Kbps";
    }

    public static string LinkSpeed(long bps)
    {
        if (bps <= 0) return "unknown speed";
        var (size, unit) = bps >= 1e9 ? Units[2] : bps >= 1e6 ? Units[1] : Units[0];
        return (bps / size).ToString("0.#", Inv) + " " + unit;
    }

    public static string Ms(double? ms) => ms is { } v ? v.ToString("0.0", Inv) + " ms" : "–";

    public static string Pct(double pct) => pct.ToString("0.0", Inv) + "%";

    /// <summary>The handful of host figures worth a glance, e.g. "Host: CPU 3% · GPU 2% · encoder 37%".</summary>
    public static string HostStats(IReadOnlyDictionary<string, string> stats)
    {
        (string Key, string Label, string Unit)[] known =
        [
            ("cpu_percent", "CPU", "%"), ("gpu_percent", "GPU", "%"), ("gpu_encoder_percent", "encoder", "%"),
            ("gpu_temp_c", "GPU", " °C"), ("ram_percent", "RAM", "%"),
        ];
        var parts = new List<string>();
        foreach (var (key, label, unit) in known)
            if (stats.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, Inv, out var value))
                parts.Add($"{label} {Math.Round(value).ToString("0", Inv)}{unit}");
        return parts.Count == 0 ? "" : "Host: " + string.Join(" · ", parts);
    }

    /// <summary>One stream in a line, e.g. "3840×2160 HEVC HDR · 120 fps · 399.0 Mbps".</summary>
    public static string Stream(StreamInfo s)
    {
        var parts = new List<string>();
        if (s.Format.Length > 0) parts.Add(s.Format);
        if (s.Fps > 0) parts.Add(s.Fps.ToString("0.#", Inv) + " fps");
        if (s.BitrateKbps > 0) parts.Add(Rate(s.BitrateKbps * 1000));
        return string.Join(" · ", parts);
    }
}
