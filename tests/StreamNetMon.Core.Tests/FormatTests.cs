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

    [Fact]
    public void HostStats_shows_the_known_figures_rounded()
    {
        var stats = new Dictionary<string, string>
        {
            ["cpu_percent"] = "3.0421981811523438", ["gpu_percent"] = "1.6782431602478027",
            ["gpu_encoder_percent"] = "36.65629196166992", ["gpu_temp_c"] = "47.099998474121094",
            ["ram_percent"] = "26.224252830266245", ["net_tx_bps"] = "2201199.5878992225",
        };
        Assert.Equal("Host: CPU 3% · GPU 2% · encoder 37% · GPU 47 °C · RAM 26%", Format.HostStats(stats));
    }

    [Fact]
    public void HostStats_with_nothing_known_is_empty() =>
        Assert.Equal("", Format.HostStats(new Dictionary<string, string> { ["other"] = "1", ["cpu_percent"] = "n/a" }));

    [Fact]
    public void Stream_line_shows_what_the_client_is_getting()
    {
        var info = new StreamInfo(398_988, 120, 3, 1, 2, "3840×2160 HEVC HDR");
        Assert.Equal("3840×2160 HEVC HDR · 120 fps · encoder set to 399.0 Mbps", Format.Stream(info));
    }
}
