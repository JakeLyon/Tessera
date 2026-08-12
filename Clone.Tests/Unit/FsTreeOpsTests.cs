using Clone.Models;
using Xunit;

namespace Clone.Tests.Unit;

public class FsTreeOpsTests
{
    private static void AssertSortedDesc(FsNode dir)
    {
        var c = dir.Children!;
        for (int i = 1; i < c.Length; i++)
            Assert.True(c[i - 1].Size >= c[i].Size, $"{dir.Name} children not sorted at {i}");
    }

    // ---- RemoveChild ----

    [Fact]
    public void RemoveChild_SplicesParentArray()
    {
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.File("a", 100), TestTree.File("b", 50), TestTree.File("c", 25)));
        var b = TestTree.Find(root, "b");

        FsTreeOps.RemoveChild(b);

        Assert.Equal(new[] { "a", "c" }, root.Children!.Select(n => n.Name));
        Assert.Equal(125, root.Size);
    }

    [Fact]
    public void RemoveChild_PropagatesToRoot()
    {
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("l1",
                TestTree.Dir("l2",
                    TestTree.File("victim", 300), TestTree.File("keep", 100)),
                TestTree.File("side", 50))));
        var victim = TestTree.Find(root, "victim");

        FsTreeOps.RemoveChild(victim);

        Assert.Equal(100, TestTree.Find(root, "l2").Size);
        Assert.Equal(150, TestTree.Find(root, "l1").Size);
        Assert.Equal(150, root.Size);
    }

    [Fact]
    public void RemoveChild_LastChild_LeavesEmptyArrayNotNull()
    {
        var root = TestTree.Seal(TestTree.Dir("root", TestTree.Dir("sub", TestTree.File("only", 10))));
        FsTreeOps.RemoveChild(TestTree.Find(root, "only"));

        var sub = TestTree.Find(root, "sub");
        Assert.NotNull(sub.Children);
        Assert.Empty(sub.Children!);
        Assert.Equal(0, sub.Size);
    }

    [Fact]
    public void RemoveChild_ResortsAncestorSiblings()
    {
        // dirA (150) > dirB (100); removing dirA's 100-file drops it to 50 → dirB must move first.
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("dirA", TestTree.File("big", 100), TestTree.File("rest", 50)),
            TestTree.Dir("dirB", TestTree.File("f", 100))));
        Assert.Equal("dirA", root.Children![0].Name);

        FsTreeOps.RemoveChild(TestTree.Find(root, "big"));

        Assert.Equal("dirB", root.Children![0].Name);
        AssertSortedDesc(root);
    }

    [Fact]
    public void RemoveChild_NullsParentOnRemovedNode()
    {
        var root = TestTree.Seal(TestTree.Dir("root", TestTree.File("a", 100), TestTree.File("b", 50)));
        var a = TestTree.Find(root, "a");

        FsTreeOps.RemoveChild(a);

        Assert.Null(a.Parent);
        Assert.Equal(100, a.Size); // size preserved — callers still report it
    }

    [Fact]
    public void RemoveChild_ThenRemoveFormerParent_SubtractsEachSizeExactlyOnce()
    {
        // Regression: a detached node used to keep its Parent chain, so removing its
        // former parent propagated the child's size into the live tree a second time.
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("P", TestTree.File("A", 100), TestTree.File("B", 50)),
            TestTree.File("keep", 10)));
        var p = TestTree.Find(root, "P");
        var a = TestTree.Find(root, "A");

        FsTreeOps.RemoveChild(a);
        Assert.Equal(50, p.Size);
        Assert.Equal(60, root.Size);

        FsTreeOps.RemoveChild(p);
        Assert.Equal(10, root.Size); // "keep" only — not 10 - 100
    }

    [Theory]
    [InlineData("root", "root", true)]
    [InlineData("sub", "root", true)]
    [InlineData("leaf", "root", true)]
    [InlineData("root", "sub", false)]
    public void IsDescendantOrSelf_WalksAncestorChain(string nodeName, string rootName, bool expected)
    {
        var root = TestTree.Seal(TestTree.Dir("root", TestTree.Dir("sub", TestTree.File("leaf", 1))));
        Assert.Equal(expected,
            FsTreeOps.IsDescendantOrSelf(TestTree.Find(root, nodeName), TestTree.Find(root, rootName)));
    }

    [Fact]
    public void IsDescendantOrSelf_NullsAreFalse()
    {
        var root = TestTree.Seal(TestTree.Dir("root", TestTree.File("f", 1)));
        Assert.False(FsTreeOps.IsDescendantOrSelf(null, root));
        Assert.False(FsTreeOps.IsDescendantOrSelf(root, null));
    }

    [Fact]
    public void RemoveChild_NullParent_NoOp()
    {
        var lone = TestTree.Seal(TestTree.Dir("root", TestTree.File("f", 10)));
        FsTreeOps.RemoveChild(lone); // root itself — must not throw
        Assert.Equal(10, lone.Size);
    }

    // ---- PropagateSizeDelta ----

    [Fact]
    public void PropagateSizeDelta_PositiveAndNegative()
    {
        var root = TestTree.Seal(TestTree.Dir("root", TestTree.Dir("sub", TestTree.File("f", 100))));
        var sub = TestTree.Find(root, "sub");

        FsTreeOps.PropagateSizeDelta(sub, 50);
        Assert.Equal(150, sub.Size);
        Assert.Equal(150, root.Size);

        FsTreeOps.PropagateSizeDelta(sub, -150);
        Assert.Equal(0, sub.Size);
        Assert.Equal(0, root.Size);
    }

    [Fact]
    public void PropagateSizeDelta_FromNull_NoOp()
        => FsTreeOps.PropagateSizeDelta(null, 1000); // must not throw

    [Fact]
    public void PropagateSizeDelta_KeepsAncestorArraysSorted()
    {
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("small", TestTree.File("s", 10)),
            TestTree.Dir("big", TestTree.File("b", 100))));
        Assert.Equal("big", root.Children![0].Name);

        FsTreeOps.PropagateSizeDelta(TestTree.Find(root, "small"), 500);

        Assert.Equal("small", root.Children![0].Name);
        AssertSortedDesc(root);
    }

    // ---- SpliceRescan ----

    [Fact]
    public void SpliceRescan_AdoptsAndReparents()
    {
        var root = TestTree.Seal(TestTree.Dir("root", TestTree.Dir("sub", TestTree.File("old", 100))));
        var sub = TestTree.Find(root, "sub");
        var fresh = TestTree.Seal(TestTree.Dir("sub", TestTree.File("new1", 60), TestTree.File("new2", 40)));

        long delta = FsTreeOps.SpliceRescan(sub, fresh);

        Assert.Equal(0, delta);
        Assert.Equal(new[] { "new1", "new2" }, sub.Children!.Select(n => n.Name));
        Assert.All(sub.Children!, c => Assert.Same(sub, c.Parent));
        Assert.Equal(100, root.Size);
    }

    [Fact]
    public void SpliceRescan_ReturnsDelta_AncestorsAdjusted()
    {
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("sub", TestTree.File("old", 100)),
            TestTree.File("side", 20)));
        var sub = TestTree.Find(root, "sub");
        var fresh = TestTree.Seal(TestTree.Dir("sub", TestTree.File("bigger", 300)));

        long delta = FsTreeOps.SpliceRescan(sub, fresh);

        Assert.Equal(200, delta);
        Assert.Equal(300, sub.Size);
        Assert.Equal(320, root.Size);
        AssertSortedDesc(root);
    }

    [Fact]
    public void SpliceRescan_ResortsOwnSiblingArray()
    {
        // Regression: PropagateSizeDelta used to start at node.Parent, which sorts
        // each ancestor's PARENT'S children — skipping the array containing the
        // resized node itself. Growing "small" past "big" must reorder them.
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.Dir("big", TestTree.File("b", 200)),
            TestTree.Dir("small", TestTree.File("s", 100))));
        Assert.Equal("big", root.Children![0].Name);

        var small = TestTree.Find(root, "small");
        FsTreeOps.SpliceRescan(small, TestTree.Seal(TestTree.Dir("small", TestTree.File("grown", 500))));

        Assert.Equal("small", root.Children![0].Name);
        AssertSortedDesc(root);
    }

    [Fact]
    public void SpliceRescan_EmptyFresh_EmptyArrayNotNull()
    {
        var root = TestTree.Seal(TestTree.Dir("root", TestTree.Dir("sub", TestTree.File("old", 100))));
        var sub = TestTree.Find(root, "sub");
        var fresh = new FsNode("sub", FsNode.FlagDir); // Children null, size 0

        long delta = FsTreeOps.SpliceRescan(sub, fresh);

        Assert.Equal(-100, delta);
        Assert.NotNull(sub.Children);
        Assert.Empty(sub.Children!);
        Assert.Equal(0, root.Size);
    }

    // ---- FindLargest ----

    private static FsNode RandomTree(int fileCount, int seed)
    {
        var rng = new Random(seed);
        // Random 3-level structure with fileCount files total.
        var dirs = new List<FsNode[]>();
        var files = Enumerable.Range(0, fileCount)
            .Select(i => TestTree.File($"f{i}", rng.Next(0, 100_000)))
            .ToArray();
        var groups = files.Chunk(17)
            .Select((chunk, i) => TestTree.Dir($"d{i}", chunk))
            .ToArray();
        return TestTree.Seal(TestTree.Dir("root", groups));
    }

    [Fact]
    public void FindLargest_MatchesLinqOracle()
    {
        var root = RandomTree(500, seed: 42);
        var expected = TestTree.Files(root)
            .OrderByDescending(f => f.Size)
            .Take(10)
            .Select(f => f.Size);

        var actual = FsTreeOps.FindLargest(root, 10).Select(f => f.Size);

        // Compare size sequences — ties make node-identity comparison invalid.
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FindLargest_KExceedsFileCount_ReturnsAllSortedDesc()
    {
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.File("a", 3), TestTree.File("b", 1), TestTree.File("c", 2)));
        var result = FsTreeOps.FindLargest(root, 100);

        Assert.Equal(new long[] { 3, 2, 1 }, result.Select(f => f.Size));
    }

    [Fact]
    public void FindLargest_Ties_ReturnsExactlyK()
    {
        var root = TestTree.Seal(TestTree.Dir("root",
            Enumerable.Range(0, 20).Select(i => TestTree.File($"f{i}", 500)).ToArray()));
        Assert.Equal(5, FsTreeOps.FindLargest(root, 5).Count);
    }

    [Fact]
    public void FindLargest_ExcludesDirsAndReparseLeaves()
    {
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.File("file", 10),
            TestTree.Reparse("junction"),          // dir-flagged leaf, Children null
            TestTree.Dir("empty")));               // dir with empty array
        var result = FsTreeOps.FindLargest(root, 10);

        Assert.Single(result);
        Assert.Equal("file", result[0].Name);
    }

    [Fact]
    public void FindLargest_EmptyTree_Empty()
        => Assert.Empty(FsTreeOps.FindLargest(TestTree.Seal(TestTree.Dir("root")), 10));

    [Fact]
    public void FindLargest_ResultIsDescending()
    {
        var root = RandomTree(200, seed: 7);
        var sizes = FsTreeOps.FindLargest(root, 50).Select(f => f.Size).ToList();
        for (int i = 1; i < sizes.Count; i++)
            Assert.True(sizes[i - 1] >= sizes[i]);
    }
}
