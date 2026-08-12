using Avalonia;
using Clone.Models;
using Clone.UI;
using Xunit;

namespace Clone.Tests.Unit;

/// <summary>
/// The layout limits exist to bound how much work the treemap does: a full drive
/// otherwise produces more rectangles than can be hit-tested cheaply on every mouse
/// move. These tests pin each cutoff, and that the default preset did not shift.
/// </summary>
public class SquarifyLimitsTests
{
    private static (List<TmRect> Rects, bool Truncated) Layout(
        FsNode dir, TreemapLimits limits, double w = 800, double h = 600)
    {
        var output = new List<TmRect>();
        bool truncated = Squarify.Layout(dir, new Rect(0, 0, w, h), 0, output, limits);
        return (output, truncated);
    }

    /// <summary>A chain of directories <paramref name="depth"/> levels deep with a file at the bottom.</summary>
    private static FsNode Chain(int depth)
    {
        FsNode node = TestTree.File("leaf", 1_000_000);
        for (int i = 0; i < depth; i++)
            node = TestTree.Dir($"d{i}", node);
        return TestTree.Seal(node);
    }

    /// <summary>One directory holding <paramref name="count"/> equally-sized files.</summary>
    private static FsNode Wide(int count) => TestTree.Seal(TestTree.Dir("root",
        Enumerable.Range(0, count).Select(i => TestTree.File($"f{i}", 1_000)).ToArray()));

    public static TheoryData<string> PresetNames => new() { "Low", "Medium", "High" };

    private static TreemapLimits Preset(string name) => name switch
    {
        "Low" => TreemapLimits.Low,
        "Medium" => TreemapLimits.Medium,
        "High" => TreemapLimits.High,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // ---- Depth ----

    [Theory]
    [MemberData(nameof(PresetNames))]
    public void EveryPreset_StopsAtItsMaxDepth(string name)
    {
        var limits = Preset(name);
        var (rects, _) = Layout(Chain(60), limits, 4000, 4000);

        Assert.NotEmpty(rects);
        Assert.All(rects, r => Assert.True(r.Depth <= limits.MaxDepth,
            $"depth {r.Depth} exceeds {name}'s MaxDepth of {limits.MaxDepth}"));
        Assert.True(rects.OrderByDescending(r => r.Depth).First().IsLeaf,
            "the node the cutoff lands on must be marked IsLeaf so it paints as a block");
    }

    [Fact]
    public void LowerPreset_ProducesNoMoreRectanglesThanAHigherOne()
    {
        var tree = Chain(60);

        int low = Layout(tree, TreemapLimits.Low, 4000, 4000).Rects.Count;
        int medium = Layout(tree, TreemapLimits.Medium, 4000, 4000).Rects.Count;
        int high = Layout(tree, TreemapLimits.High, 4000, 4000).Rects.Count;

        Assert.True(low <= medium, $"Low produced {low}, Medium {medium}");
        Assert.True(medium <= high, $"Medium produced {medium}, High {high}");
    }

    // ---- Rectangle cap ----

    [Fact]
    public void WideTree_OutputIsCappedAndReportedAsTruncated()
    {
        // Fan-out larger than the whole budget: the only place the hard stop in
        // FlushRow bites, since there is no deeper level to decline to descend into.
        var limits = TreemapLimits.Low;
        var (rects, truncated) = Layout(Wide(limits.MaxRects * 2), limits, 4000, 4000);

        Assert.True(truncated);
        Assert.Equal(limits.MaxRects, rects.Count);
    }

    [Fact]
    public void CapIsNeverExceeded_ForAnyPreset()
    {
        foreach (var limits in new[] { TreemapLimits.Low, TreemapLimits.Medium, TreemapLimits.High })
        {
            var (rects, _) = Layout(Wide(limits.MaxRects + 500), limits, 4000, 4000);
            Assert.True(rects.Count <= limits.MaxRects,
                $"emitted {rects.Count} with a cap of {limits.MaxRects}");
        }
    }

    [Fact]
    public void SmallTree_IsNotReportedAsTruncated()
    {
        var tree = TestTree.Seal(TestTree.Dir("root",
            TestTree.File("a", 500), TestTree.File("b", 300), TestTree.File("c", 200)));

        var (rects, truncated) = Layout(tree, TreemapLimits.Medium);

        Assert.False(truncated);
        Assert.Equal(3, rects.Count);
    }

    [Fact]
    public void CapStopsTheDescent_WithoutLeavingHolesInDrawnLevels()
    {
        // Every emitted rectangle must either be a leaf or have its children present:
        // a parent whose children were dropped has to be painted as a solid block.
        var limits = TreemapLimits.Low with { MaxRects = 200 };
        var (rects, truncated) = Layout(Chain(6), limits, 4000, 4000);

        Assert.False(truncated);   // a chain cannot exhaust 200 rectangles
        foreach (var rect in rects.Where(r => !r.IsLeaf))
            Assert.Contains(rects, other => ReferenceEquals(other.Node.Parent, rect.Node));
    }

    // ---- The default must not shift ----

    [Fact]
    public void Medium_MatchesTheCutoffsTheLayoutHasAlwaysUsed()
    {
        Assert.Equal(4, TreemapLimits.Medium.MinSide);
        Assert.Equal(20, TreemapLimits.Medium.MinArea);
        Assert.Equal(24, TreemapLimits.Medium.MaxDepth);
    }

    [Fact]
    public void OmittingLimits_IsIdenticalToPassingMedium()
    {
        var tree = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("docs", TestTree.File("big.bin", 50_000), TestTree.File("small.txt", 900)),
            TestTree.Dir("media", TestTree.Dir("clips", TestTree.File("a.mp4", 30_000))),
            TestTree.File("loose.dat", 7_000)));

        var withDefault = new List<TmRect>();
        Squarify.Layout(tree, new Rect(0, 0, 800, 600), 0, withDefault);

        var (withMedium, _) = Layout(tree, TreemapLimits.Medium);

        Assert.Equal(withDefault, withMedium);
    }
}
