using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Tessera.Models;
using Tessera.UI;
using Xunit;

namespace Tessera.Tests.Headless;

/// <summary>
/// The control side of the detail limits: changing them must re-lay out, and the
/// truncation signal must fire on transitions only — the layout is recomputed during
/// render, so a per-render event would rewrite the status bar every frame.
/// </summary>
public class TreemapLimitsTests
{
    private static (Window Window, TreemapControl Map) Host(FsNode root)
    {
        var map = new TreemapControl();
        var window = new Window { Width = 800, Height = 600, Content = map };
        window.Show();
        map.RootNode = root;
        return (window, map);
    }

    /// <summary>A deep chain, so the depth cutoff is what separates the presets.</summary>
    private static FsNode DeepTree()
    {
        FsNode node = TestTree.File("leaf", 1_000_000);
        for (int i = 0; i < 40; i++)
            node = TestTree.Dir($"d{i}", node);
        return TestTree.Seal(node);
    }

    private static FsNode WideTree(int count) => TestTree.Seal(TestTree.Dir("root",
        Enumerable.Range(0, count).Select(i => TestTree.File($"f{i}", 1_000)).ToArray()));

    [AvaloniaFact]
    public void DefaultLimits_AreMedium()
    {
        var (_, map) = Host(DeepTree());
        Assert.Equal(TreemapLimits.Medium, map.Limits);
    }

    [AvaloniaFact]
    public void LoweringLimits_RecomputesLayoutWithFewerRectangles()
    {
        var (_, map) = Host(DeepTree());
        int atMedium = map.EnsureLayout().Count;

        map.Limits = TreemapLimits.Low;
        int atLow = map.EnsureLayout().Count;

        Assert.True(atLow < atMedium, $"Low produced {atLow}, Medium {atMedium}");
    }

    [AvaloniaFact]
    public void RaisingLimits_RecomputesLayoutWithMoreRectangles()
    {
        var (_, map) = Host(DeepTree());
        map.Limits = TreemapLimits.Low;
        int atLow = map.EnsureLayout().Count;

        map.Limits = TreemapLimits.High;
        int atHigh = map.EnsureLayout().Count;

        Assert.True(atHigh > atLow, $"High produced {atHigh}, Low {atLow}");
    }

    [AvaloniaFact]
    public void SettingTheSameLimits_DoesNotInvalidate()
    {
        var (_, map) = Host(DeepTree());
        var before = map.EnsureLayout();

        map.Limits = TreemapLimits.Medium;

        // Same list instance, not merely an equal one: no recompute happened.
        Assert.Same(before, map.EnsureLayout());
    }

    [AvaloniaFact]
    public void TruncationEvent_FiresOnceOnEachTransition_NotPerLayout()
    {
        var limits = TreemapLimits.Low;
        var (_, map) = Host(WideTree(limits.MaxRects * 2));
        var events = new List<bool>();
        map.LayoutTruncatedChanged += events.Add;

        map.Limits = limits;
        map.EnsureLayout();
        map.EnsureLayout();          // idempotent — layout is not dirty
        map.InvalidateLayout();
        map.EnsureLayout();          // recomputed, but still truncated

        Assert.Equal(new[] { true }, events);
        Assert.True(map.LayoutTruncated);
    }

    [AvaloniaFact]
    public void TruncationClears_WhenTheLimitIsRaisedEnough()
    {
        // Comfortably under High's cap, comfortably over Low's.
        var (_, map) = Host(WideTree(TreemapLimits.Low.MaxRects + 2_000));
        var events = new List<bool>();

        map.Limits = TreemapLimits.Low;
        map.EnsureLayout();
        map.LayoutTruncatedChanged += events.Add;

        map.Limits = TreemapLimits.High;
        map.EnsureLayout();

        Assert.Equal(new[] { false }, events);
        Assert.False(map.LayoutTruncated);
    }

    [AvaloniaFact]
    public void HitTest_StillWorksAgainstATruncatedLayout()
    {
        var limits = TreemapLimits.Low;
        var (_, map) = Host(WideTree(limits.MaxRects * 2));
        map.Limits = limits;
        var layout = map.EnsureLayout();

        var target = layout[0];
        Assert.Same(target.Node, map.HitTest(target.Bounds.Center));
    }
}
