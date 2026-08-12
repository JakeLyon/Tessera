using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Clone.Models;
using Clone.UI;
using Xunit;

namespace Clone.Tests.Headless;

public class TreemapInteractionTests
{
    private static (Window Window, TreemapControl Map, FsNode Root) Host()
    {
        var root = TestTree.Seal(
            TestTree.Dir(@"C:\scan",
                TestTree.File("alpha.bin", 6000),
                TestTree.File("beta.bin", 4000)));
        var map = new TreemapControl();
        var window = new Window { Width = 800, Height = 600, Content = map };
        window.Show();
        map.RootNode = root;
        return (window, map, root);
    }

    [AvaloniaFact]
    public void Hover_MouseMove_RaisesHoverChangedAndSetsTooltip()
    {
        var (window, map, _) = Host();
        var target = map.EnsureLayout().First(t => t.Node.Name == "alpha.bin");

        FsNode? hovered = null;
        map.HoverChanged += n => hovered = n;

        var point = map.TranslatePoint(target.Bounds.Center, window)!.Value;
        window.MouseMove(point);

        Assert.NotNull(hovered);
        Assert.Equal("alpha.bin", hovered!.Name);

        string? tip = ToolTip.GetTip(map) as string;
        Assert.NotNull(tip);
        Assert.Contains(@"C:\scan\alpha.bin", tip);
        Assert.Contains("5.86 KB", tip); // 6000 bytes formatted
    }

    [AvaloniaFact]
    public void Hover_MovingBetweenNodes_FiresPerNodeOnce()
    {
        var (window, map, _) = Host();
        var layout = map.EnsureLayout();
        var a = layout.First(t => t.Node.Name == "alpha.bin");
        var b = layout.First(t => t.Node.Name == "beta.bin");

        var fired = new List<string?>();
        map.HoverChanged += n => fired.Add(n?.Name);

        window.MouseMove(map.TranslatePoint(a.Bounds.Center, window)!.Value);
        window.MouseMove(map.TranslatePoint(a.Bounds.Center + new Vector(1, 1), window)!.Value); // same node — no event
        window.MouseMove(map.TranslatePoint(b.Bounds.Center, window)!.Value);

        Assert.Equal(new[] { "alpha.bin", "beta.bin" }, fired);
    }

    [AvaloniaFact]
    public void Resize_RecomputesLayoutToNewBounds()
    {
        var (window, map, _) = Host();
        var before = map.EnsureLayout().ToList();
        double beforeArea = before.Sum(t => t.Bounds.Width * t.Bounds.Height);

        // Shrink the control itself — a headless Window's client size does not
        // track Width/Height changes synchronously, but control sizing does.
        map.Width = 400;
        map.Height = 300;
        window.UpdateLayout();

        var after = map.EnsureLayout().ToList();
        double afterArea = after.Sum(t => t.Bounds.Width * t.Bounds.Height);

        Assert.True(afterArea < beforeArea, "layout did not shrink with the window");
        var newBounds = new Rect(map.Bounds.Size).Deflate(1).Inflate(1e-6);
        Assert.All(after, t => Assert.True(newBounds.Contains(t.Bounds.BottomRight)));
    }

    [AvaloniaFact]
    public void NullRoot_SafeEverywhere()
    {
        var map = new TreemapControl();
        var window = new Window { Width = 300, Height = 200, Content = map };
        window.Show();

        Assert.Empty(map.EnsureLayout());
        Assert.Null(map.HitTest(new Point(150, 100)));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // render with no root — no throw
    }

    [AvaloniaFact]
    public void EmptyDirRoot_NoRects_NoThrow()
    {
        var map = new TreemapControl();
        var window = new Window { Width = 300, Height = 200, Content = map };
        window.Show();
        map.RootNode = TestTree.Seal(TestTree.Dir(@"C:\empty"));

        Assert.Empty(map.EnsureLayout());
        Assert.Null(map.HitTest(new Point(150, 100)));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    [AvaloniaFact]
    public void DoubleClick_RaisesNodeDoubleClicked()
    {
        var (window, map, _) = Host();
        var target = map.EnsureLayout().First(t => t.Node.Name == "alpha.bin");

        FsNode? doubleClicked = null;
        map.NodeDoubleClicked += n => doubleClicked = n;

        var point = map.TranslatePoint(target.Bounds.Center, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        // Headless input synthesizes ClickCount like a real pointer (rapid same-spot
        // clicks aggregate); if this ever proves platform-flaky, drop to manual list.
        Assert.NotNull(doubleClicked);
        Assert.Equal("alpha.bin", doubleClicked!.Name);
    }
}
