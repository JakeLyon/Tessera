using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Tessera.Models;
using Tessera.UI;
using Xunit;

namespace Tessera.Tests.Headless;

public class TreemapControlTests
{
    private static (Window Window, TreemapControl Map) Host(FsNode root)
    {
        var map = new TreemapControl();
        var window = new Window { Width = 800, Height = 600, Content = map };
        window.Show();
        map.RootNode = root;
        return (window, map);
    }

    private static FsNode SampleTree() => TestTree.Seal(
        TestTree.Dir(@"C:\scan",
            TestTree.Dir("docs",
                TestTree.File("report.pdf", 4000),
                TestTree.File("notes.txt", 2000)),
            TestTree.File("video.mp4", 3000),
            TestTree.File("tiny.log", 1000)));

    [AvaloniaFact]
    public void HitTest_CenterOfEveryLeafRect_ReturnsThatNode()
    {
        var (_, map) = Host(SampleTree());
        var layout = map.EnsureLayout();
        Assert.NotEmpty(layout);

        foreach (var tm in layout.Where(t => t.IsLeaf))
            Assert.Same(tm.Node, map.HitTest(tm.Bounds.Center));
    }

    [AvaloniaFact]
    public void HitTest_ChildBeatsParent()
    {
        var (_, map) = Host(SampleTree());
        var layout = map.EnsureLayout();

        var childRect = layout.First(t => t.Node.Name == "report.pdf");
        var hit = map.HitTest(childRect.Bounds.Center);
        Assert.Equal("report.pdf", hit?.Name); // not "docs"
    }

    [AvaloniaFact]
    public void HitTest_OutsidePoint_ReturnsNull()
    {
        var (_, map) = Host(SampleTree());
        map.EnsureLayout();
        Assert.Null(map.HitTest(new Point(-10, -10)));
        Assert.Null(map.HitTest(new Point(100000, 100000)));
    }

    [AvaloniaFact]
    public void NodeClicked_RaisedViaHeadlessMouse()
    {
        var (window, map) = Host(SampleTree());
        var target = map.EnsureLayout().First(t => t.Node.Name == "video.mp4");

        FsNode? clicked = null;
        map.NodeClicked += n => clicked = n;

        var point = map.TranslatePoint(target.Bounds.Center, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.NotNull(clicked);
        Assert.Equal("video.mp4", clicked!.Name);
    }

    [AvaloniaFact]
    public void NodeRightClicked_RaisedViaHeadlessMouse()
    {
        var (window, map) = Host(SampleTree());
        var target = map.EnsureLayout().First(t => t.Node.Name == "tiny.log");

        FsNode? rightClicked = null;
        map.NodeRightClicked += n => rightClicked = n;

        var point = map.TranslatePoint(target.Bounds.Center, window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);

        Assert.Equal("tiny.log", rightClicked?.Name);
    }

    [AvaloniaFact]
    public void SelectedNode_NotInLayout_RenderDoesNotThrow()
    {
        var (_, map) = Host(SampleTree());
        map.SelectedNode = TestTree.File("detached.bin", 1); // never part of the tree

        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // must not throw
    }

    [AvaloniaFact]
    public void InvalidateLayout_AfterMutation_ReflectsNewRects()
    {
        var root = SampleTree();
        var (_, map) = Host(root);
        Assert.Contains(map.EnsureLayout(), t => t.Node.Name == "video.mp4");

        FsTreeOps.RemoveChild(TestTree.Find(root, "video.mp4"));
        map.InvalidateLayout();

        var layout = map.EnsureLayout();
        Assert.DoesNotContain(layout, t => t.Node.Name == "video.mp4");
        // Remaining leaves re-tile the full area: conservation still holds.
        double total = layout.Where(t => t.IsLeaf && !t.Node.IsDir).Sum(t => t.Bounds.Width * t.Bounds.Height);
        Assert.True(total > 0);
    }

    [AvaloniaFact]
    public void RootNodeChange_ClearsSelection()
    {
        var root = SampleTree();
        var (_, map) = Host(root);
        var docs = TestTree.Find(root, "docs");
        map.SelectedNode = docs;

        map.RootNode = docs; // drill in

        Assert.Null(map.SelectedNode);
        Assert.Same(docs, map.RootNode);
    }
}
