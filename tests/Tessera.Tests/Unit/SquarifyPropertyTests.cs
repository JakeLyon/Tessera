using Avalonia;
using Tessera.Models;
using Tessera.UI;
using Tessera.Treemap;
using Xunit;

namespace Tessera.Tests.Unit;

/// <summary>
/// Property-style tests: seeded random trees laid out into rects of varied aspect
/// ratios must always satisfy the treemap invariants, whatever the shape.
/// </summary>
public class SquarifyPropertyTests
{
    private static FsNode RandomTree(Random rng, int depth = 0)
    {
        int childCount = rng.Next(1, depth >= 2 ? 6 : 9);
        var children = new FsNode[childCount];
        for (int i = 0; i < childCount; i++)
        {
            bool makeDir = depth < 2 && rng.NextDouble() < 0.35;
            children[i] = makeDir
                ? RandomTree(rng, depth + 1)
                : TestTree.File($"f{depth}_{i}_{rng.Next(1000)}", rng.Next(0, 10_000));
        }
        return TestTree.Dir($"d{depth}_{rng.Next(1000)}", children);
    }

    public static TheoryData<int, double, double> Cases()
    {
        var data = new TheoryData<int, double, double>();
        var shapes = new (double W, double H)[] { (400, 300), (1200, 80), (80, 1200), (250, 250), (37, 613) };
        foreach (int seed in new[] { 1, 7, 42, 1337 })
            foreach (var (w, h) in shapes)
                data.Add(seed, w, h);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Invariants_HoldForRandomTreesAndShapes(int seed, double w, double h)
    {
        var root = TestTree.Seal(RandomTree(new Random(seed)));
        var output = new List<TmRect>();
        Squarify.Layout(root, new Rect(0, 0, w, h), 0, output);

        // 1. All bounds finite and non-negative.
        foreach (var tm in output)
        {
            Assert.False(double.IsNaN(tm.Bounds.Width) || double.IsInfinity(tm.Bounds.Width));
            Assert.False(double.IsNaN(tm.Bounds.Height) || double.IsInfinity(tm.Bounds.Height));
            Assert.True(tm.Bounds.Width >= 0 && tm.Bounds.Height >= 0);
        }

        // Group laid-out rects by the dir whose Layout call produced them.
        var byParent = output.GroupBy(t => t.Node.Parent!);
        var rectOf = output.ToDictionary(t => t.Node, t => t.Bounds);

        foreach (var group in byParent)
        {
            var siblings = group.ToList();
            // Parent's available rect: the whole input rect for root children,
            // else the parent's own rect deflated by the 1px nesting inset.
            Rect avail = ReferenceEquals(group.Key, root)
                ? new Rect(0, 0, w, h)
                : rectOf[group.Key].Deflate(1);
            long groupTotal = group.Key.Children!.Sum(c => c.Size);

            for (int i = 0; i < siblings.Count; i++)
            {
                var a = siblings[i].Bounds;

                // 2. Containment in the parent's available area.
                Assert.True(avail.Inflate(1e-6).Contains(a.TopLeft), "escapes parent (TL)");
                Assert.True(avail.Inflate(1e-6).Contains(a.BottomRight), "escapes parent (BR)");

                // 3. Proportionality within the sibling group.
                double expected = (double)siblings[i].Node.Size / groupTotal * (avail.Width * avail.Height);
                Assert.Equal(expected, a.Width * a.Height, precision: 3);

                // 4. No sibling overlap.
                for (int j = i + 1; j < siblings.Count; j++)
                {
                    var inter = a.Intersect(siblings[j].Bounds);
                    double interArea = inter.Width <= 0 || inter.Height <= 0 ? 0 : inter.Width * inter.Height;
                    Assert.True(interArea < 1e-6, "sibling rects overlap");
                }
            }

            // 5. Sibling areas tile the parent's available area exactly
            //    (zero-size children excluded by the algorithm, contribute 0 anyway).
            double tiled = siblings.Sum(t => t.Bounds.Width * t.Bounds.Height);
            Assert.Equal(avail.Width * avail.Height, tiled, precision: 3);
        }
    }
}
