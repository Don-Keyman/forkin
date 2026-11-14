namespace StreamNetMon.Core.Tests;

public class AlertGateTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    static readonly Verdict Bad = new(VerdictLevel.Warning, "Wi-Fi hop to iPad is degraded");

    [Fact]
    public void Fires_once_after_10s()
    {
        var gate = new AlertGate();
        var fired = new List<int>();
        for (var i = 0; i <= 30; i++)
            if (gate.ShouldNotify(Bad, T0.AddSeconds(i))) fired.Add(i);
        Assert.Equal([11], fired);
    }

    [Fact]
    public void Does_not_fire_for_short_blips()
    {
        var gate = new AlertGate();
        for (var i = 0; i < 60; i++)
        {
            var v = i % 12 < 9 ? Bad : Verdict.AllClear;   // 9 s bad, 3 s fine, repeating
            Assert.False(gate.ShouldNotify(v, T0.AddSeconds(i)));
        }
    }

    [Fact]
    public void Rearms_after_recovery()
    {
        var gate = new AlertGate();
        var fired = 0;
        for (var i = 0; i <= 20; i++) if (gate.ShouldNotify(Bad, T0.AddSeconds(i))) fired++;
        Assert.False(gate.ShouldNotify(Verdict.AllClear, T0.AddSeconds(21)));
        for (var i = 22; i <= 40; i++) if (gate.ShouldNotify(Bad, T0.AddSeconds(i))) fired++;
        Assert.Equal(2, fired);
    }

    [Fact]
    public void A_changed_problem_does_not_fire_again_without_recovery()
    {
        var gate = new AlertGate();
        var fired = 0;
        for (var i = 0; i <= 20; i++) if (gate.ShouldNotify(Bad, T0.AddSeconds(i))) fired++;
        var other = new Verdict(VerdictLevel.Problem, "Desktop adapter problem");
        for (var i = 21; i <= 40; i++) if (gate.ShouldNotify(other, T0.AddSeconds(i))) fired++;
        Assert.Equal(1, fired);
    }
}
