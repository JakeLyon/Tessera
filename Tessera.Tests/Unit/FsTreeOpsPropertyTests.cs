using Tessera.Models;
using Xunit;

namespace Tessera.Tests.Unit;

/// <summary>
/// Randomized mutation sequences: after every RemoveChild / SpliceRescan /
/// PropagateSizeDelta, the tree must still satisfy the app-wide invariants
/// (correct Parent links, dir Size == sum of children, arrays sorted size-desc).
/// </summary>
public class FsTreeOpsPropertyTests
{
    private static void AssertInvariants(FsNode node)
    {
        if (node.Children is not { } children)
            return;
        long sum = 0;
        for (int i = 0; i < children.Length; i++)
        {
            Assert.Same(node, children[i].Parent);
            if (i > 0)
                Assert.True(children[i - 1].Size >= children[i].Size, $"unsorted under {node.Name}");
            AssertInvariants(children[i]);
            sum += children[i].Size;
        }
        Assert.Equal(sum, node.Size);
    }

    private static FsNode RandomTree(Random rng, string prefix, int depth = 0)
    {
        int childCount = rng.Next(2, 7);
        var children = new FsNode[childCount];
        for (int i = 0; i < childCount; i++)
        {
            children[i] = depth < 3 && rng.NextDouble() < 0.4
                ? RandomTree(rng, $"{prefix}_{i}", depth + 1)
                : TestTree.File($"{prefix}_f{i}", rng.Next(0, 5_000));
        }
        return TestTree.Dir($"{prefix}_d", children);
    }

    private static List<FsNode> AllNodes(FsNode root)
    {
        var list = new List<FsNode>();
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            list.Add(n);
            if (n.Children is { } c)
                foreach (var child in c)
                    stack.Push(child);
        }
        return list;
    }

    [Theory]
    [InlineData(3)]
    [InlineData(21)]
    [InlineData(99)]
    public void RandomRemovalSequence_InvariantsHoldThroughout(int seed)
    {
        var rng = new Random(seed);
        var root = TestTree.Seal(RandomTree(rng, "r"));
        AssertInvariants(root);

        for (int step = 0; step < 40; step++)
        {
            var candidates = AllNodes(root).Where(n => n.Parent is not null).ToList();
            if (candidates.Count == 0)
                break;

            FsTreeOps.RemoveChild(candidates[rng.Next(candidates.Count)]);
            AssertInvariants(root);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(77)]
    public void RandomSpliceSequence_InvariantsHoldThroughout(int seed)
    {
        var rng = new Random(seed);
        var root = TestTree.Seal(RandomTree(rng, "r"));

        for (int step = 0; step < 15; step++)
        {
            var dirs = AllNodes(root).Where(n => n.IsDir && n.Parent is not null).ToList();
            if (dirs.Count == 0)
                break;

            var target = dirs[rng.Next(dirs.Count)];
            var fresh = TestTree.Seal(RandomTree(rng, $"fresh{step}"));

            long before = target.Size;
            long delta = FsTreeOps.SpliceRescan(target, fresh);

            Assert.Equal(fresh.Size - before, delta);
            AssertInvariants(root);
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(63)]
    public void MixedOpSequence_InvariantsHoldThroughout(int seed)
    {
        var rng = new Random(seed);
        var root = TestTree.Seal(RandomTree(rng, "r"));

        for (int step = 0; step < 30; step++)
        {
            var nodes = AllNodes(root).Where(n => n.Parent is not null).ToList();
            if (nodes.Count == 0)
                break;
            var target = nodes[rng.Next(nodes.Count)];

            switch (rng.Next(3))
            {
                case 0:
                    FsTreeOps.RemoveChild(target);
                    break;
                case 1 when target.IsDir:
                    FsTreeOps.SpliceRescan(target, TestTree.Seal(RandomTree(rng, $"fr{step}")));
                    break;
                default:
                    // Simulates a single file growing/shrinking on disk. Only valid on
                    // leaves: PropagateSizeDelta's contract is "a change of `delta`
                    // happened below/at this node", so calling it on an unchanged dir
                    // would rightfully violate the sum invariant.
                    long delta = rng.Next(-200, 500);
                    if (target.Children is null && target.Size + delta >= 0)
                        FsTreeOps.PropagateSizeDelta(target, delta);
                    break;
            }
            AssertInvariants(root);
        }
    }
}
