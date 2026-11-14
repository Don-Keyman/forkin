namespace StreamNetMon.Core;

public interface IClock
{
    DateTimeOffset Now { get; }
}

public interface IPinger
{
    /// <summary>Round-trip time in milliseconds, or null when there was no reply. May throw.</summary>
    Task<double?> PingAsync(string host, TimeSpan timeout, CancellationToken ct);
}
