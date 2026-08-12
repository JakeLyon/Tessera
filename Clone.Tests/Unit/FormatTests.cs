using Clone.Util;
using Xunit;

namespace Clone.Tests.Unit;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(532, "532 B")]
    [InlineData(1023, "1023 B")]
    public void Bytes_BelowOneKb_PlainBytes(long input, string expected)
        => Assert.Equal(expected, Format.Bytes(input));

    [Fact]
    public void Bytes_1024_IsOneKbTwoDecimals()
        => Assert.Equal("1.00 KB", Format.Bytes(1024));

    [Fact]
    public void Bytes_FormatSwitch_F2ToF1()
    {
        // 10239/1024 = 9.999… → F2; 10240/1024 = 10 exactly → F1.
        Assert.Equal("10.00 KB", Format.Bytes(10_239));
        Assert.Equal("10.0 KB", Format.Bytes(10_240));
    }

    [Fact]
    public void Bytes_FormatSwitch_F1ToF0()
        => Assert.Equal("100 KB", Format.Bytes(102_400));

    [Fact]
    public void Bytes_JustUnderOneMb_DocumentsRolloverQuirk()
    {
        // 1 048 575 B = 1023.999 KB, formatted with F0 → "1024 KB" (not "1.00 MB").
        Assert.Equal("1024 KB", Format.Bytes(1_048_575));
        Assert.Equal("1.00 MB", Format.Bytes(1_048_576));
    }

    [Theory]
    [InlineData(1L << 30, "1.00 GB")]
    [InlineData(1L << 40, "1.00 TB")]
    [InlineData(1L << 50, "1.00 PB")]
    [InlineData((1L << 30) + (1L << 28), "1.25 GB")]
    public void Bytes_HigherUnits(long input, string expected)
        => Assert.Equal(expected, Format.Bytes(input));

    [Fact]
    public void Bytes_LongMax_ClampsAtPb()
        => Assert.Equal("8192 PB", Format.Bytes(long.MaxValue));

    [Fact]
    public void Bytes_Negative_DocumentsCurrentBehavior()
        // Negative sizes never occur in scanned trees; they fall into the "< 1024" branch.
        => Assert.Equal("-1 B", Format.Bytes(-1));

    [Theory]
    [InlineData(0.0, "0.0%")]
    [InlineData(0.123, "12.3%")]
    [InlineData(0.5, "50.0%")]
    [InlineData(1.0, "100.0%")]
    public void Percent_FormatsOneDecimal(double fraction, string expected)
        => Assert.Equal(expected, Format.Percent(fraction));

    [Theory]
    [InlineData(0)]
    [InlineData(1_234)]
    [InlineData(1_234_567)]
    public void Count_MatchesN0_CultureNeutral(long n)
        => Assert.Equal(n.ToString("N0"), Format.Count(n));
}
