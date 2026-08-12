using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Tessera.Models;
using Tessera.UI;
using Xunit;

namespace Tessera.Tests.Headless;

public class TopFilesWindowTests
{
    private static FsNode SampleTree() => TestTree.Seal(
        TestTree.Dir(@"C:\scan",
            TestTree.Dir("docs",
                TestTree.File("huge.iso", 90_000),
                TestTree.File("mid.zip", 50_000)),
            TestTree.File("video.mp4", 70_000),
            TestTree.File("tiny.log", 10)));

    private static FlatTreeDataGridSource<FsNode> SourceOf(TopFilesWindow window)
    {
        var grid = Assert.IsType<TreeDataGrid>(window.Content);
        return Assert.IsType<FlatTreeDataGridSource<FsNode>>(grid.Source);
    }

    [AvaloniaFact]
    public void Window_TitleContainsRootPath()
    {
        var window = new TopFilesWindow(SampleTree());
        Assert.Contains(@"C:\scan", window.Title);
        Assert.Contains("100", window.Title);
    }

    [AvaloniaFact]
    public void Rows_AreLargestFilesSortedDescending()
    {
        var window = new TopFilesWindow(SampleTree());
        window.Show();

        var items = SourceOf(window).Items.ToList();

        Assert.Equal(new[] { "huge.iso", "video.mp4", "mid.zip", "tiny.log" },
            items.Select(f => f.Name));
        Assert.Equal(4, items.Count); // fewer files than the top-100 cap → all listed
    }

    [AvaloniaFact]
    public void Rows_ScopedToGivenSubtree()
    {
        var root = SampleTree();
        var docs = TestTree.Find(root, "docs");

        var window = new TopFilesWindow(docs);
        window.Show();

        var names = SourceOf(window).Items.Select(f => f.Name).ToList();
        Assert.Equal(new[] { "huge.iso", "mid.zip" }, names);
        Assert.Contains("docs", window.Title);
    }
}
