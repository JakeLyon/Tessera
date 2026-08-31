using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Tessera.Models;
using Tessera.UI;
using Tessera.Util;
using Tessera.Treemap;
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
    // View ▸ Colour, and free space
    // =====================================================================

    [AvaloniaFact]
    public void ColourMenu_HasOneItemPerChoice_DepthCheckedByDefault()
    {
        var (window, _) = Host();

        Assert.Equal(MainWindow.ColorChoices.Length, window.ColorMenuItems.Count);
        Assert.Equal(TreemapColorMode.Depth, window.Treemap.ColorMode);

        var only = Assert.Single(window.ColorMenuItems, i => i.IsChecked);
        Assert.Contains("Depth", only.Header!.ToString());
    }

    [AvaloniaFact]
    public void ColourMenu_ChoosingAModeAppliesItToTheTreemap()
    {
        var (window, _) = Host();

        for (int i = 0; i < MainWindow.ColorChoices.Length; i++)
        {
            window.ColorMenuItems[i].RaiseEvent(
                new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(MainWindow.ColorChoices[i].Mode, window.Treemap.ColorMode);
        }
    }

    [AvaloniaFact]
    public void ColourMenu_ItemsShareARadioGroup_SoTheChoiceIsExclusive()
    {
        var (window, _) = Host();

        Assert.All(window.ColorMenuItems, item =>
        {
            Assert.Equal(MenuItemToggleType.Radio, item.ToggleType);
            Assert.Equal("TreemapColour", item.GroupName);
        });
    }

    [AvaloniaFact]
    public void FreeSpaceMenu_IsOffByDefault_AndTogglesTheTreemap()
    {
        var (window, _) = Host();
        Assert.False(window.Treemap.ShowFreeSpace);
        Assert.Equal(MenuItemToggleType.CheckBox, window.FreeSpaceMenuItem.ToggleType);

        window.FreeSpaceMenuItem.IsChecked = true;
        window.FreeSpaceMenuItem.RaiseEvent(
            new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.True(window.Treemap.ShowFreeSpace);
    }

    /// <summary>
    /// The C:\ bug, kept as a guard after the feature that caused it was removed. A
    /// truncation notice used to be raised from inside EnsureLayout, which Render calls,
    /// so the handler wrote to a TextBlock mid-pass; Avalonia threw "Visual was
    /// invalidated during the render pass", the renderer stopped compositing, and the
    /// window froze on its last frame with the scan apparently hung. Nothing writes to
    /// another visual from the layout path now, and this pins that over a layout big
    /// enough to be realistic, while colours and free space change under it.
    /// </summary>
    [AvaloniaFact]
    public void BigLayout_RendersRepeatedly_WithoutKillingTheRenderPass()
    {
        var (window, _) = Host();
        var wide = TestTree.Seal(TestTree.Dir(@"C:\wide",
            Enumerable.Range(0, 30_000).Select(i => TestTree.File($"f{i}", 1_000)).ToArray()));
        window.LoadTree(wide);

        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        string summary = window.StatusText;

        window.Treemap.ColorMode = TreemapColorMode.Extension;
        window.Treemap.FreeSpaceBytes = 5_000_000;
        window.Treemap.ShowFreeSpace = true;

        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        // Still rendering, and the scan summary was never overwritten from the render path.
        Assert.Equal(summary, window.StatusText);
        Assert.NotEmpty(window.Treemap.EnsureLayout());
    }
}
