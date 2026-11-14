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
}
