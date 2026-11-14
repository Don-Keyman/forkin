namespace StreamNetMon.Core;

/// <summary>Raw cumulative counters of a network adapter.</summary>
public sealed record AdapterReading(string Name, long LinkSpeedBps, long BytesSent, long BytesReceived, long OutErrors, long OutDiscards);

public sealed record AdapterStats(string Name, long LinkSpeedBps, long UsualLinkSpeedBps,
    double SendBps, double ReceiveBps, long OutErrorsDelta, long OutDiscardsDelta);

/// <summary>Turns successive counter readings into rates and deltas.</summary>
public sealed class AdapterTracker
{
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);

    private AdapterReading? _previous;
    private DateTimeOffset _previousAt;
    private string? _usualName;
    private long _usualSpeed;

    public AdapterStats? Update(AdapterReading? reading, DateTimeOffset now)
    {
        if (reading is null)
        {
            _previous = null;
            return null;
        }

        if (_usualName != reading.Name)
        {
            _usualName = reading.Name;
            _usualSpeed = 0;
            _previous = null;
        }
        _usualSpeed = Math.Max(_usualSpeed, reading.LinkSpeedBps);

        double send = 0, receive = 0;
        long errors = 0, discards = 0;
        var elapsed = (now - _previousAt).TotalSeconds;
        if (_previous is { } p && elapsed > 0 && now - _previousAt <= MaxGap)
        {
            var sent = reading.BytesSent - p.BytesSent;
            var received = reading.BytesReceived - p.BytesReceived;
            var err = reading.OutErrors - p.OutErrors;
            var disc = reading.OutDiscards - p.OutDiscards;
            // Any counter going backwards means the driver reset them: skip this interval entirely.
            if (sent >= 0 && received >= 0 && err >= 0 && disc >= 0)
            {
                send = sent * 8 / elapsed;
                receive = received * 8 / elapsed;
                errors = err;
                discards = disc;
            }
        }

        _previous = reading;
        _previousAt = now;
        return new AdapterStats(reading.Name, reading.LinkSpeedBps, _usualSpeed, send, receive, errors, discards);
    }
}
