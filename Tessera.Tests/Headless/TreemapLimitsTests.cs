using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
    public void DefaultLimits_AreFull()
    {
        var (_, map) = Host(DeepTree());
        Assert.Equal(TreemapLimits.Full, map.Limits);
    }

    [AvaloniaFact]
    public void LoweringLimits_RecomputesLayoutWithFewerRectangles()
    {
        var (_, map) = Host(DeepTree());
        int atDefault = map.EnsureLayout().Count;

        map.Limits = TreemapLimits.Low;
        int atLow = map.EnsureLayout().Count;

        Assert.True(atLow < atDefault, $"Low produced {atLow}, the default {atDefault}");
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

        map.Limits = TreemapLimits.Full;   // already the default

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

        // The flag updates immediately; the notification is posted, because
        // EnsureLayout runs inside the render pass and handlers touch other visuals.
        Assert.True(map.LayoutTruncated);
        Assert.Empty(events);

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { true }, events);
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
        Dispatcher.UIThread.RunJobs();

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

    /// <summary>
    /// The point of making Full the default: a tree that Medium would have cut short —
    /// and told the user about — now lays out whole, with no truncation note. Sized
    /// comfortably past Medium's 20,000 cap and well under Full's 500,000.
    /// </summary>
    [AvaloniaFact]
    public void AtTheDefault_ATreeThatWouldOverflowMedium_IsNotTruncated()
    {
        var (_, map) = Host(WideTree(TreemapLimits.Medium.MaxRects * 3));

        int atDefault = map.EnsureLayout().Count;
        Assert.False(map.LayoutTruncated);
        Assert.True(atDefault > TreemapLimits.Medium.MaxRects,
            $"expected more than Medium's cap, got {atDefault}");

        // The same tree at the old default is cut short and says so.
        map.Limits = TreemapLimits.Medium;
        map.EnsureLayout();
        Assert.True(map.LayoutTruncated);
    }
}
