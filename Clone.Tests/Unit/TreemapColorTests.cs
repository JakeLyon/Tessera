using Clone.UI;
using Xunit;

namespace Clone.Tests.Unit;

public class TreemapColorTests
{
    [Fact]
    public void Fnv1a_StableAcrossCalls()
    {
        // Pin the algorithm: same input → same hash, distinct inputs differ.
        Assert.Equal(TreemapControl.Fnv1a(".txt"), TreemapControl.Fnv1a(".txt"));
        Assert.Equal(TreemapControl.Fnv1a(""), TreemapControl.Fnv1a(""));
        Assert.NotEqual(TreemapControl.Fnv1a(".txt"), TreemapControl.Fnv1a(".dll"));
    }

    [Fact]
    public void Fnv1a_EmptyString_IsOffsetBasis()
        => Assert.Equal(unchecked((int)2166136261), TreemapControl.Fnv1a(""));

    [Theory]
    [InlineData(".txt")]
    [InlineData(".dll")]
    [InlineData("")]
    [InlineData(".文件")]
    public void Fnv1a_HueMapping_InRange(string ext)
    {
        double hue = (TreemapControl.Fnv1a(ext) % 360 + 360) % 360;
        Assert.InRange(hue, 0, 359);
    }

    [Fact]
    public void Hsl_PrimaryAnchors()
    {
        var red = TreemapControl.Hsl(0, 1, 0.5);
        Assert.Equal((255, 0, 0), (red.R, red.G, red.B));

        var green = TreemapControl.Hsl(120, 1, 0.5);
        Assert.Equal((0, 255, 0), (green.R, green.G, green.B));

        var white = TreemapControl.Hsl(0, 0, 1);
        Assert.Equal((255, 255, 255), (white.R, white.G, white.B));
    }

    [Fact]
    public void Hsl_FullSweep_NoOverflow()
    {
        // Constructing the Color proves every channel lands in byte range.
        for (int h = 0; h < 360; h += 3)
            foreach (double s in new[] { 0.0, 0.5, 1.0 })
                foreach (double l in new[] { 0.0, 0.42, 0.58, 1.0 })
                    _ = TreemapControl.Hsl(h, s, l);
    }
}
