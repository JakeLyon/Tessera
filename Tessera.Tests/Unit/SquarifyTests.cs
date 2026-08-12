using Avalonia;
using Tessera.Models;
using Tessera.UI;
using Xunit;

namespace Tessera.Tests.Unit;

public class SquarifyTests
{
    private static List<TmRect> Layout(FsNode dir, double w = 400, double h = 300)
    {
        var output = new List<TmRect>();
        Squarify.Layout(dir, new Rect(0, 0, w, h), 0, output);
        return output;
    }

    private static double Area(Rect r) => r.Width * r.Height;

    private static double IntersectionArea(Rect a, Rect b)
    {
        var i = a.Intersect(b);
        return i.Width <= 0 || i.Height <= 0 ? 0 : Area(i);
    }

    // ---- Core invariants ----

    [Fact]
    public void Layout_AreaConservation_FlatDir()
    {
        var dir = TestTree.Seal(TestTree.Dir("d",
            TestTree.File("a", 500), TestTree.File("b", 300), TestTree.File("c", 200)));
        var rects = Layout(dir);

        double total = rects.Sum(t => Area(t.Bounds));
        Assert.Equal(400 * 300, total, precision: 3);
    }

    [Fact]
    public void Layout_Proportionality()
    {
        var dir = TestTree.Seal(TestTree.Dir("d",
            TestTree.File("a", 600), TestTree.File("b", 250), TestTree.File("c", 150)));
        var rects = Layout(dir);

        foreach (var tm in rects)
        {
            double expected = (double)tm.Node.Size / dir.Size * (400 * 300);
            Assert.Equal(expected, Area(tm.Bounds), precision: 3);
        }
    }

    [Fact]
    public void Layout_NoOverlap_AllWithinBounds()
    {
        var dir = TestTree.Seal(TestTree.Dir("d",
            Enumerable.Range(1, 12).Select(i => TestTree.File($"f{i}", i * 37)).ToArray()));
        var rects = Layout(dir);
        var bounds = new Rect(0, 0, 400, 300);

        for (int i = 0; i < rects.Count; i++)
        {
            Assert.True(bounds.Inflate(1e-6).Contains(rects[i].Bounds.TopLeft));
            Assert.True(bounds.Inflate(1e-6).Contains(rects[i].Bounds.BottomRight));
            for (int j = i + 1; j < rects.Count; j++)
                Assert.True(IntersectionArea(rects[i].Bounds, rects[j].Bounds) < 1e-6,
                    $"rects {i} and {j} overlap");
        }
    }

    [Fact]
    public void Layout_ParentFirstOrdering_BackwardsScanFindsDeepest()
    {
        var dir = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("sub",
                TestTree.File("deep1", 5000), TestTree.File("deep2", 3000)),
            TestTree.File("top", 2000)));
        var rects = Layout(dir);

        // Every child rect appears after its parent dir's rect.
        var indexOf = rects.Select((t, i) => (t.Node, i)).ToDictionary(x => x.Node, x => x.i);
        foreach (var tm in rects)
            if (tm.Node.Parent is { } p && indexOf.TryGetValue(p, out int pi))
                Assert.True(indexOf[tm.Node] > pi);

        // Backwards scan at a nested leaf's center returns the leaf, not the dir.
        var deepRect = rects.First(t => t.Node.Name == "deep1").Bounds;
        var center = deepRect.Center;
        FsNode? hit = null;
        for (int i = rects.Count - 1; i >= 0; i--)
            if (rects[i].Bounds.Contains(center)) { hit = rects[i].Node; break; }
        Assert.Equal("deep1", hit?.Name);
    }

    [Fact]
    public void Layout_NestedChildren_InsideDeflatedParent()
    {
        var dir = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("sub", TestTree.File("a", 100), TestTree.File("b", 50)),
            TestTree.File("c", 30)));
        var rects = Layout(dir);

        var subRect = rects.First(t => t.Node.Name == "sub").Bounds;
        var inner = subRect.Deflate(1).Inflate(1e-6);
        foreach (var tm in rects.Where(t => t.Node.Parent?.Name == "sub"))
        {
            Assert.True(inner.Contains(tm.Bounds.TopLeft));
            Assert.True(inner.Contains(tm.Bounds.BottomRight));
        }
    }

    // ---- Edge cases ----

    [Fact]
    public void Layout_ZeroSizeChildren_GetNoRect()
    {
        var dir = TestTree.Seal(TestTree.Dir("d",
            TestTree.File("a", 100), TestTree.File("b", 50),
            TestTree.File("z1", 0), TestTree.File("z2", 0)));
        Assert.Equal(2, Layout(dir).Count);
    }

    [Fact]
    public void Layout_AllZeroSizes_Empty()
    {
        var dir = TestTree.Seal(TestTree.Dir("d", TestTree.File("a", 0), TestTree.File("b", 0)));
        Assert.Empty(Layout(dir));
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(400, 1)]
    [InlineData(1, 1)]
    public void Layout_DegenerateRect_Empty(double w, double h)
    {
        var dir = TestTree.Seal(TestTree.Dir("d", TestTree.File("a", 100)));
        Assert.Empty(Layout(dir, w, h));
    }

    [Fact]
    public void Layout_FileNode_NoOutput()
        => Assert.Empty(Layout(TestTree.File("f", 100)));

    [Fact]
    public void Layout_EmptyDir_NoOutput()
        => Assert.Empty(Layout(TestTree.Dir("d")));

    [Fact]
    public void Layout_SingleChild_FillsRect()
    {
        var dir = TestTree.Seal(TestTree.Dir("d", TestTree.File("only", 100)));
        var rects = Layout(dir);
        Assert.Single(rects);
        Assert.Equal(new Rect(0, 0, 400, 300), rects[0].Bounds);
    }

    [Fact]
    public void Layout_FourEqualInSquare_QuartersWithGoodAspect()
    {
        var dir = TestTree.Seal(TestTree.Dir("d",
            Enumerable.Range(1, 4).Select(i => TestTree.File($"f{i}", 100)).ToArray()));
        var rects = Layout(dir, 200, 200);

        Assert.Equal(4, rects.Count);
        foreach (var tm in rects)
        {
            Assert.Equal(100 * 100, Area(tm.Bounds), precision: 3);
            double aspect = tm.Bounds.Width / tm.Bounds.Height;
            Assert.InRange(aspect, 0.99, 1.01); // squarify should produce perfect quarters
        }
    }

    // ---- Recursion cutoffs ----

    [Fact]
    public void Layout_MaxDepth_StopsAt24()
    {
        // 30-deep chain of dirs, file at the bottom.
        FsNode leaf = TestTree.File("leaf", 1_000_000);
        FsNode node = leaf;
        for (int i = 0; i < 30; i++)
            node = TestTree.Dir($"d{i}", node);
        var root = TestTree.Seal(node);

        var rects = Layout(root, 2000, 2000);
        Assert.NotEmpty(rects);
        Assert.All(rects, t => Assert.True(t.Depth <= 24));
        var deepest = rects.OrderByDescending(t => t.Depth).First();
        Assert.True(deepest.IsLeaf, "the cutoff node must be marked IsLeaf");
    }

    [Fact]
    public void Layout_TinyDirRect_NotRecursedInto()
    {
        // One dominant file forces the dir into a sliver below MinSide.
        var dir = TestTree.Seal(TestTree.Dir("root",
            TestTree.File("huge", 1_000_000),
            TestTree.Dir("tiny", TestTree.File("inner", 10))));
        var rects = Layout(dir, 100, 100);

        var tiny = rects.First(t => t.Node.Name == "tiny");
        Assert.True(tiny.IsLeaf);
        Assert.DoesNotContain(rects, t => t.Node.Name == "inner");
    }

    // ---- Pathological ----

    [Fact]
    public void Layout_Pathological_1000x2_AllFinite()
    {
        var dir = TestTree.Seal(TestTree.Dir("d",
            Enumerable.Range(1, 20).Select(i => TestTree.File($"f{i}", i)).ToArray()));
        var rects = Layout(dir, 1000, 2);

        Assert.NotEmpty(rects);
        foreach (var tm in rects)
        {
            Assert.False(double.IsNaN(tm.Bounds.Width) || double.IsInfinity(tm.Bounds.Width));
            Assert.False(double.IsNaN(tm.Bounds.Height) || double.IsInfinity(tm.Bounds.Height));
            Assert.True(tm.Bounds.Width >= 0 && tm.Bounds.Height >= 0);
        }
    }

    [Fact]
    public void Layout_1000TinyChildren_ConservationHolds()
    {
        var dir = TestTree.Seal(TestTree.Dir("d",
            Enumerable.Range(1, 1000).Select(i => TestTree.File($"f{i}", 1)).ToArray()));
        var rects = Layout(dir, 100, 100);

        Assert.Equal(1000, rects.Count);
        double total = rects.Sum(t => Area(t.Bounds));
        Assert.Equal(100 * 100, total, precision: 3);
        Assert.All(rects, t => Assert.False(double.IsNaN(Area(t.Bounds))));
    }
}
