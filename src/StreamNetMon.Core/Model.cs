namespace StreamNetMon.Core;

public enum ConnectionType { Wired, WiFi }

public enum TargetRole { StreamingDevice, Router, Gateway }

public sealed record Device(Guid Id, string Name, string Host, ConnectionType Type, TargetRole Role);

/// <summary>One probe. <see cref="RttMs"/> is null when there was no reply.</summary>
public readonly record struct ProbeResult(DateTimeOffset At, double? RttMs);

public sealed record TargetStats(double? LatencyMs, double? JitterMs, double LossPct, bool Offline, int Samples);
