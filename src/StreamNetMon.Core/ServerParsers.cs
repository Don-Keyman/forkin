using System.Globalization;
using System.Net;
using System.Text.Json;

namespace StreamNetMon.Core;

/// <summary>
/// Reads the streaming server's JSON without depending on its exact shape: forks rename and
/// nest fields, so these look for the first array of objects and for any of several field names.
/// </summary>
internal static class JsonWalk
{
    public static JsonDocument? TryParse(string text)
    {
        try { return JsonDocument.Parse(text); }
        catch (JsonException) { return null; }
    }

    /// <summary>The objects of the first array (breadth-first) that contains at least one object.</summary>
    public static IReadOnlyList<JsonElement> FirstObjectArray(JsonElement root)
    {
        var queue = new Queue<JsonElement>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var e = queue.Dequeue();
            if (e.ValueKind == JsonValueKind.Array)
            {
                var objects = e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList();
                if (objects.Count > 0) return objects;
                foreach (var x in e.EnumerateArray()) queue.Enqueue(x);
            }
            else if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject()) queue.Enqueue(p.Value);
            }
        }
        return [];
    }

    /// <summary>The first scalar found under any of <paramref name="keys"/>, preferring earlier keys and shallower depth.</summary>
    public static JsonElement? Find(JsonElement obj, params string[] keys)
    {
        foreach (var key in keys)
            if (FindKey(obj, key, depth: 0) is { } found) return found;
        return null;
    }

    private static JsonElement? FindKey(JsonElement obj, string key, int depth)
    {
        if (obj.ValueKind != JsonValueKind.Object || depth > 3) return null;
        foreach (var p in obj.EnumerateObject())
            if (p.NameEquals(key) && p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return p.Value;
        foreach (var p in obj.EnumerateObject())
            if (FindKey(p.Value, key, depth + 1) is { } nested) return nested;
        return null;
    }
}

public static class VibepolloParser
{
    private static readonly string[] NameKeys = ["name", "client_name", "device_name"];
    private static readonly string[] AddressKeys = ["address", "ip", "client_ip", "remote_address", "peer"];
    private static readonly string[] StartKeys = ["started_at", "start_time"];

    public static IReadOnlyList<StreamSession> Sessions(string json)
    {
        using var doc = JsonWalk.TryParse(json);
        if (doc is null) return [];
        return JsonWalk.FirstObjectArray(doc.RootElement)
            .Select(o => new StreamSession(
                Text(JsonWalk.Find(o, NameKeys)) ?? "Unknown device",
                Address(Text(JsonWalk.Find(o, AddressKeys))),
                Time(JsonWalk.Find(o, StartKeys))))
            .ToList();
    }

    public static IReadOnlyList<string> PairedNames(string json) => SunshineParser.PairedNames(json);

    /// <summary>Top-level scalar values as text; nested values are left out.</summary>
    public static IReadOnlyDictionary<string, string> HostStats(string json)
    {
        using var doc = JsonWalk.TryParse(json);
        var stats = new Dictionary<string, string>();
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object) return stats;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var text = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
            if (text is not null) stats[p.Name] = text;
        }
        return stats;
    }

    /// <summary>Strips a port, IPv6 brackets and an IPv4-mapped prefix from an address.</summary>
    public static string? Address(string? raw)
    {
        raw = raw?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        if (IPAddress.TryParse(raw, out var ip) || (IPEndPoint.TryParse(raw, out var ep) && (ip = ep.Address) is not null))
            return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
        var colon = raw.IndexOf(':');
        return colon > 0 && colon == raw.LastIndexOf(':') ? raw[..colon] : raw;   // hostname:port
    }

    private static string? Text(JsonElement? e) => e?.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(e.Value.GetString()) ? null : e.Value.GetString(),
        JsonValueKind.Number => e.Value.GetRawText(),
        _ => null,
    };

    private static DateTimeOffset? Time(JsonElement? e)
    {
        if (e is not { } v) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && n > 0)
        {
            // Seconds or milliseconds since 1970: anything this large can only be milliseconds.
            return n > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds((long)n) : DateTimeOffset.FromUnixTimeSeconds((long)n);
        }
        return v.ValueKind == JsonValueKind.String
               && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : null;
    }
}

public static class SunshineParser
{
    public static IReadOnlyList<string> PairedNames(string json)
    {
        using var doc = JsonWalk.TryParse(json);
        if (doc is null) return [];
        return JsonWalk.FirstObjectArray(doc.RootElement)
            .Select(o => o.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();
    }
}
