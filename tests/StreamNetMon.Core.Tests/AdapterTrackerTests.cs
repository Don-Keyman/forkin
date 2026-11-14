namespace StreamNetMon.Core.Tests;

public class AdapterTrackerTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    const long G25 = 2_500_000_000;

    static AdapterReading R(long sent, long recv = 0, long err = 0, long disc = 0, long speed = G25, string name = "Ethernet") =>
        new(name, speed, sent, recv, err, disc);

    [Fact]
    public void Rate_from_two_readings()
    {
        var t = new AdapterTracker();
        t.Update(R(0), T0);
        var s = t.Update(R(1_250_000, 125_000), T0.AddSeconds(1))!;
        Assert.Equal(10_000_000, s.SendBps);
        Assert.Equal(1_000_000, s.ReceiveBps);
    }

    [Fact]
    public void First_reading_has_zero_rate()
    {
        var s = new AdapterTracker().Update(R(999_999_999), T0)!;
        Assert.Equal(0, s.SendBps);
        Assert.Equal(0, s.OutErrorsDelta);
    }

    [Fact]
    public void Counter_going_backwards_gives_zero_not_negative()
    {
        var t = new AdapterTracker();
        t.Update(R(5_000_000, err: 7), T0);
        var s = t.Update(R(1_000, err: 0), T0.AddSeconds(1))!;
        Assert.Equal(0, s.SendBps);
        Assert.Equal(0, s.OutErrorsDelta);
        var next = t.Update(R(126_000), T0.AddSeconds(2))!;
        Assert.Equal(1_000_000, next.SendBps);
    }

    [Fact]
    public void Long_gap_gives_zero_rate()
    {
        var t = new AdapterTracker();
        t.Update(R(0), T0);
        var s = t.Update(R(900_000_000), T0.AddSeconds(30))!;
        Assert.Equal(0, s.SendBps);
    }

    [Fact]
    public void Adapter_change_resets_baseline()
    {
        var t = new AdapterTracker();
        t.Update(R(0), T0);
        var s = t.Update(R(5_000_000, name: "Wi-Fi", speed: 866_000_000), T0.AddSeconds(1))!;
        Assert.Equal(0, s.SendBps);
        Assert.Equal("Wi-Fi", s.Name);
        Assert.Equal(866_000_000, s.UsualLinkSpeedBps);
    }

    [Fact]
    public void Null_reading_gives_null()
    {
        var t = new AdapterTracker();
        t.Update(R(0), T0);
        Assert.Null(t.Update(null, T0.AddSeconds(1)));
        var s = t.Update(R(5_000_000), T0.AddSeconds(2))!;
        Assert.Equal(0, s.SendBps);
    }

    [Fact]
    public void Usual_speed_remembers_highest()
    {
        var t = new AdapterTracker();
        t.Update(R(0), T0);
        var s = t.Update(R(0, speed: 100_000_000), T0.AddSeconds(1))!;
        Assert.Equal(100_000_000, s.LinkSpeedBps);
        Assert.Equal(G25, s.UsualLinkSpeedBps);
    }

    [Fact]
    public void Error_and_discard_deltas()
    {
        var t = new AdapterTracker();
        t.Update(R(0, err: 2, disc: 10), T0);
        var s = t.Update(R(0, err: 5, disc: 14), T0.AddSeconds(1))!;
        Assert.Equal(3, s.OutErrorsDelta);
        Assert.Equal(4, s.OutDiscardsDelta);
    }
}
