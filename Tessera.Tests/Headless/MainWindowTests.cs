using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Tessera.Models;
using Tessera.UI;
using Xunit;

namespace Tessera.Tests.Headless;

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

    // =====================================================================
    // Help ▸ About
    // =====================================================================

    /// <summary>
    /// The third-party notices are embedded in the assembly precisely so a lone exe
    /// still carries them, which only helps if there is a way to reach them. This is it.
    /// </summary>
    [AvaloniaFact]
    public void HelpMenu_AboutOpensTheAboutWindow()
    {
        var (window, _) = Host();

        Assert.Contains("About", window.AboutMenuItem.Header!.ToString());

        window.AboutMenuItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();   // Guarded runs the body on the dispatcher

        var about = Assert.Single(window.OwnedWindows.OfType<AboutWindow>());
        Assert.NotEmpty(about.NoticesText);
        about.Close();
    }

    // =====================================================================
    // View ▸ Detail
    // =====================================================================

    [AvaloniaFact]
    public void DetailMenu_HasOneItemPerPreset_FullCheckedByDefault()
    {
        var (window, _) = Host();

        Assert.Equal(MainWindow.DetailPresets.Length, window.DetailMenuItems.Count);
        Assert.Equal(TreemapLimits.Full, window.Treemap.Limits);

        var checkedItems = window.DetailMenuItems.Where(i => i.IsChecked).ToList();
        var only = Assert.Single(checkedItems);
        Assert.Contains("Full", only.Header!.ToString());
    }

    [AvaloniaFact]
    public void DetailMenu_ChoosingAPreset_AppliesItToTheTreemap()
    {
        var (window, _) = Host();

        for (int i = 0; i < MainWindow.DetailPresets.Length; i++)
        {
            var item = window.DetailMenuItems[i];
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(MainWindow.DetailPresets[i].Limits, window.Treemap.Limits);
        }
    }

    [AvaloniaFact]
    public void DetailMenu_ItemsShareARadioGroup_SoTheChoiceIsExclusive()
    {
        var (window, _) = Host();

        Assert.All(window.DetailMenuItems, item =>
        {
            Assert.Equal(MenuItemToggleType.Radio, item.ToggleType);
            Assert.Equal("TreemapDetail", item.GroupName);
        });
    }

    [AvaloniaFact]
    public void TruncationNote_AppearsAndClears_WithoutTouchingTheScanSummary()
    {
        var (window, _) = Host();
        // More files in one directory than Low's whole rectangle budget.
        var wide = TestTree.Seal(TestTree.Dir(@"C:\wide",
            Enumerable.Range(0, TreemapLimits.Low.MaxRects * 2)
                .Select(i => TestTree.File($"f{i}", 1_000)).ToArray()));
        window.LoadTree(wide);

        string summary = window.StatusText;
        Assert.Equal("", window.DetailNoteText);

        window.Treemap.Limits = TreemapLimits.Low;
        window.Treemap.EnsureLayout();
        Dispatcher.UIThread.RunJobs();   // the note is posted, not written inline

        Assert.Contains("raise View", window.DetailNoteText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(summary, window.StatusText);   // the scan summary must survive

        window.Treemap.Limits = TreemapLimits.High;
        window.Treemap.EnsureLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("", window.DetailNoteText);
        Assert.Equal(summary, window.StatusText);
    }

    /// <summary>
    /// The C:\ bug. A layout that overflows the rectangle cap flips the truncation
    /// flag, and the flag is computed inside EnsureLayout — which Render calls. Raising
    /// the event inline meant the handler wrote to the detail-note TextBlock in the
    /// middle of the render pass, and Avalonia threw "Visual was invalidated during
    /// the render pass". The renderer then stopped compositing: the scan finished, but
    /// the window froze on its last frame and never repainted again.
    /// Only scans large enough to overflow the active rectangle cap ever reached it.
    /// </summary>
    [AvaloniaFact]
    public void TruncationDuringRender_DoesNotKillTheRenderPass()
    {
        var (window, _) = Host();
        var wide = TestTree.Seal(TestTree.Dir(@"C:\wide",
            Enumerable.Range(0, TreemapLimits.Low.MaxRects * 2)
                .Select(i => TestTree.File($"f{i}", 1_000)).ToArray()));
        window.Treemap.Limits = TreemapLimits.Low;
        window.LoadTree(wide);

        // A real render pass, which is what calls EnsureLayout and trips the flag.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.Treemap.LayoutTruncated);
        Assert.Contains("raise View", window.DetailNoteText, StringComparison.OrdinalIgnoreCase);

        // Still rendering: a second pass must work too, and the note must not flicker.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("raise View", window.DetailNoteText, StringComparison.OrdinalIgnoreCase);
    }
}
