namespace StreamNetMon.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Core_assembly_loads() => Assert.NotNull(typeof(StreamNetMon.Core.Device).Assembly);
}
