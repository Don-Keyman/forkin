namespace StreamNetMon.Core.Tests;

public sealed class FakeClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
}
