namespace StreamNetMon.Core.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(38_200_000, "38.2 Mbps")]
    [InlineData(0, "0 Kbps")]
    [InlineData(999, "1.0 Kbps")]
    [InlineData(1_500, "1.5 Kbps")]
    [InlineData(999_950, "1.0 Mbps")]
    [InlineData(2_500_000_000, "2.5 Gbps")]
    [InlineData(1_000_000_000, "1.0 Gbps")]
    [InlineData(100_000_000, "100.0 Mbps")]
    public void Rate_scales_units(double bps, string expected) => Assert.Equal(expected, Format.Rate(bps));

    [Theory]
    [InlineData(2_500_000_000, "2.5 Gbps")]
    [InlineData(1_000_000_000, "1 Gbps")]
    [InlineData(100_000_000, "100 Mbps")]
    public void LinkSpeed_drops_trailing_zero(long bps, string expected) => Assert.Equal(expected, Format.LinkSpeed(bps));

    [Fact]
    public void Ms_null_is_dash() => Assert.Equal("–", Format.Ms(null));

    [Fact]
    public void Ms_has_one_decimal() => Assert.Equal("14.0 ms", Format.Ms(14.04));

    [Fact]
    public void Pct_has_one_decimal() => Assert.Equal("2.1%", Format.Pct(2.08));

    [Fact]
    public void Formatting_ignores_the_system_culture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("0.9 ms", Format.Ms(0.9));
            Assert.Equal("38.2 Mbps", Format.Rate(38_200_000));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }
}
