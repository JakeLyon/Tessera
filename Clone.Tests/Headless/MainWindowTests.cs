using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Clone.Models;
using Clone.UI;
using Xunit;

namespace Clone.Tests.Headless;

public class MainWindowTests
{
    private static (MainWindow Window, FsNode Root) Host()
    {
        var root = TestTree.Seal(
            TestTree.Dir(@"C:\scan",
                TestTree.Dir("docs",
                    TestTree.Dir("archive",
                        TestTree.File("old.zip", 5000)),
                    TestTree.File("report.pdf", 4000)),
                TestTree.File("video.mp4", 3000),
                TestTree.Reparse("junction")));
        var window = new MainWindow();
        window.Show();
        window.LoadTree(root);
        return (window, root);
    }

    [AvaloniaFact]
    public void LoadTree_RootRowPresentAndExpanded()
    {
        var (window, root) = Host();

        Assert.NotNull(window.TreeSource);
        Assert.Same(root, window.TreeSource!.Items.Single());
        // SetTreeSource expands the root row; its children appear as rows.
        Assert.True(window.TreeSource.Rows.Count > 1);
    }

    [AvaloniaFact]
    public void LoadTree_TreemapRootAndCrumbSet_UpDisabled()
    {
        var (window, root) = Host();

        Assert.Same(root, window.Treemap.RootNode);
        Assert.Equal(root.GetFullPath(), window.CrumbText);
        Assert.False(window.UpEnabled);
    }

    [AvaloniaFact]
    public void TreeSelection_SyncsToTreemap()
    {
        var (window, root) = Host();
        var video = TestTree.Find(root, "video.mp4");

        window.TreeSource!.RowSelection!.SelectedIndex = new IndexPath(
            0, Array.IndexOf(root.Children!, video));

        Assert.Same(video, window.Treemap.SelectedNode);
    }

    [AvaloniaFact]
    public void TreemapSelection_SyncsToTree_DeepLeafRoundTrip()
    {
        var (window, root) = Host();
        var deep = TestTree.Find(root, "old.zip"); // depth 3

        window.SelectFromTreemap(deep, drill: false);

        Assert.Same(deep, window.TreeSource!.RowSelection!.SelectedItem);
        Assert.Same(deep, window.Treemap.SelectedNode);
    }

    [AvaloniaFact]
    public void TreemapSelection_FirstAndLastChildren_RoundTrip()
    {
        var (window, root) = Host();

        foreach (var node in new[] { root.Children![0], root.Children![^1] })
        {
            window.SelectFromTreemap(node, drill: false);
            Assert.Same(node, window.TreeSource!.RowSelection!.SelectedItem);
        }
    }

    [AvaloniaFact]
    public void Drill_UpdatesCrumbAndUp_NavigateUpReturns()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");

        window.SelectFromTreemap(docs, drill: true);
        Assert.Same(docs, window.Treemap.RootNode);
        Assert.Equal(docs.GetFullPath(), window.CrumbText);
        Assert.True(window.UpEnabled);

        window.NavigateUp();
        Assert.Same(root, window.Treemap.RootNode);
        Assert.False(window.UpEnabled);
    }

    [AvaloniaFact]
    public void TreeSelection_OutsideDrilledRoot_PopsTreemapToScanRoot()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");
        var video = TestTree.Find(root, "video.mp4");

        window.SelectFromTreemap(docs, drill: true);
        Assert.Same(docs, window.Treemap.RootNode);

        // Selecting a node outside "docs" in the tree must pop the treemap back out.
        window.TreeSource!.RowSelection!.SelectedIndex = new IndexPath(
            0, Array.IndexOf(root.Children!, video));

        Assert.Same(root, window.Treemap.RootNode);
        Assert.Same(video, window.Treemap.SelectedNode);
    }

    [AvaloniaFact]
    public void FileDoubleClick_DoesNotDrill()
    {
        var (window, root) = Host();
        var video = TestTree.Find(root, "video.mp4");

        window.SelectFromTreemap(video, drill: true); // drill on a file is a no-op

        Assert.Same(root, window.Treemap.RootNode);
        Assert.Same(video, window.Treemap.SelectedNode);
    }

    // ---- Context menu decision logic (pure static; no menu needs to open) ----

    public static TheoryData<string, bool, bool, bool, bool, bool> CtxCases() => new()
    {
        // node kind,   scanning, Show,  CanDelete, CanRescan, CanTop
        { "null",       false,    false, false,     false,     false },
        { "dir",        true,     false, false,     false,     false },
        { "root",       false,    true,  false,     true,      true },
        { "dir",        false,    true,  true,      true,      true },
        { "file",       false,    true,  true,      false,     false },
        { "reparse",    false,    true,  true,      false,     false },
    };

    [AvaloniaTheory]
    [MemberData(nameof(CtxCases))]
    public void GetContextMenuState_Matrix(string kind, bool scanning,
        bool show, bool canDelete, bool canRescan, bool canTop)
    {
        var root = TestTree.Seal(
            TestTree.Dir(@"C:\scan",
                TestTree.Dir("dir", TestTree.File("f", 10)),
                TestTree.File("file", 5),
                TestTree.Reparse("reparse")));

        FsNode? node = kind switch
        {
            "null" => null,
            "root" => root,
            _ => TestTree.Find(root, kind),
        };

        var state = MainWindow.GetContextMenuState(node, scanning);

        Assert.Equal(show, state.Show);
        Assert.Equal(canDelete, state.CanDelete);
        Assert.Equal(canRescan, state.CanRescan);
        Assert.Equal(canTop, state.CanTopFiles);
    }

    [AvaloniaFact]
    public void LoadTree_Twice_ReplacesModelCleanly()
    {
        var (window, _) = Host();
        var second = TestTree.Seal(
            TestTree.Dir(@"D:\other",
                TestTree.File("solo.bin", 42)));

        window.LoadTree(second);

        Assert.Same(second, window.TreeSource!.Items.Single());
        Assert.Same(second, window.Treemap.RootNode);
        Assert.Equal(@"D:\other", window.CrumbText);
        Assert.False(window.UpEnabled);

        // Sync still works against the new model.
        var solo = TestTree.Find(second, "solo.bin");
        window.SelectFromTreemap(solo, drill: false);
        Assert.Same(solo, window.TreeSource!.RowSelection!.SelectedItem);
    }

    [AvaloniaFact]
    public void DrillTwoLevels_UpTwice_ReturnsToRoot()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");
        var archive = TestTree.Find(root, "archive");

        window.SelectFromTreemap(docs, drill: true);
        window.SelectFromTreemap(archive, drill: true);
        Assert.Same(archive, window.Treemap.RootNode);
        Assert.Equal(archive.GetFullPath(), window.CrumbText);

        window.NavigateUp();
        Assert.Same(docs, window.Treemap.RootNode);
        window.NavigateUp();
        Assert.Same(root, window.Treemap.RootNode);
        Assert.False(window.UpEnabled);
    }

    [AvaloniaFact]
    public void NoSyncFeedbackLoop_SelectionChangedFiresOncePerAction()
    {
        var (window, root) = Host();
        var video = TestTree.Find(root, "video.mp4");

        int fired = 0;
        window.TreeSource!.RowSelection!.SelectionChanged += (_, _) => fired++;

        window.SelectFromTreemap(video, drill: false);

        Assert.Equal(1, fired);
    }
}
