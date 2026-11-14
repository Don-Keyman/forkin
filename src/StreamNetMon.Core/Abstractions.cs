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

public interface IAdapterSource
{
    /// <summary>Current counters of the adapter carrying the stream, or null when there is none.</summary>
    AdapterReading? Read();
}

public interface ISecretProtector
{
    string Protect(string plain);

    /// <summary>The original text, or null when it cannot be read back (other user, damaged value).</summary>
    string? Unprotect(string stored);
}
