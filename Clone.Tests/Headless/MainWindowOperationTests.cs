using Avalonia.Headless.XUnit;
using Clone.Models;
using Clone.Scanning;
using Clone.UI;
using Xunit;

namespace Clone.Tests.Headless;

/// <summary>
/// Delete and rescan drive the tree through async boundaries where the model can
/// change underneath them. These tests replace the scanner and the confirmation
/// dialog with deterministic fakes, so no disk I/O or modal window is involved.
/// </summary>
public class MainWindowOperationTests
{
    private static FsNode SampleTree() => TestTree.Seal(
        TestTree.Dir(@"C:\scan",
            TestTree.Dir("docs",
                TestTree.Dir("archive",
                    TestTree.File("old.zip", 5000)),
                TestTree.File("report.pdf", 4000)),
            TestTree.File("video.mp4", 3000)));

    /// <summary>A scanner whose completion the test controls.</summary>
    private sealed class FakeScanner
    {
        public readonly TaskCompletionSource<FsNode> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }

        public Task<FsNode> Scan(string path, ScanProgress progress, CancellationToken ct)
        {
            Calls++;
            Token = ct;
            return Completion.Task;
        }
    }

    private static (MainWindow Window, FsNode Root) Host(bool confirmDeletes = true)
    {
        var window = new MainWindow
        {
            ConfirmDelete = _ => Task.FromResult(confirmDeletes),
            ReportProblem = (_, _) => Task.CompletedTask,
        };
        window.Show();
        var root = SampleTree();
        window.LoadTree(root);
        return (window, root);
    }

    // =====================================================================
    // Rescan
    // =====================================================================

    [AvaloniaFact]
    public async Task RescanNode_CancelledMidScan_TreeUnchangedAndStatusSaysCancelled()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");
        long sizeBefore = docs.Size;
        long rootBefore = root.Size;

        var fake = new FakeScanner();
        window.ScanFunc = fake.Scan;

        var rescan = window.RescanNodeAsync(docs);
        window.CancelCurrentScan();
        // A cancelled scan still returns a partial tree — here, a drastic undercount.
        fake.Completion.SetResult(TestTree.Seal(TestTree.Dir("docs", TestTree.File("partial.tmp", 1))));
        await rescan;

        Assert.Equal(sizeBefore, docs.Size);
        Assert.Equal(rootBefore, root.Size);
        Assert.Contains("cancelled", window.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "archive", "report.pdf" }, docs.Children!.Select(c => c.Name).OrderBy(n => n));
    }

    [AvaloniaFact]
    public async Task RescanNode_TreemapDrilledIntoDescendant_ResetsTreemapRootToRescannedNode()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");
        var archive = TestTree.Find(root, "archive");

        window.SelectFromTreemap(archive, drill: true);
        Assert.Same(archive, window.Treemap.RootNode);

        var fake = new FakeScanner();
        window.ScanFunc = fake.Scan;
        var rescan = window.RescanNodeAsync(docs);
        fake.Completion.SetResult(TestTree.Seal(TestTree.Dir("docs", TestTree.File("fresh.bin", 900))));
        await rescan;

        // The old "archive" node is orphaned; the treemap must not still be rooted there.
        Assert.Same(docs, window.Treemap.RootNode);

        // And selection must still round-trip (a stale root produced -1 index paths).
        var fresh = docs.Children!.Single();
        window.SelectFromTreemap(fresh, drill: false);
        Assert.Same(fresh, window.TreeSource!.RowSelection!.SelectedItem);
    }

    [AvaloniaFact]
    public async Task RescanNode_AfterLoadTreeReplacedModel_DiscardsResult()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");

        var fake = new FakeScanner();
        window.ScanFunc = fake.Scan;
        var rescan = window.RescanNodeAsync(docs);

        // A new scan lands while the rescan is in flight.
        var replacement = TestTree.Seal(TestTree.Dir(@"D:\other", TestTree.File("solo.bin", 42)));
        window.LoadTree(replacement);

        fake.Completion.SetResult(TestTree.Seal(TestTree.Dir("docs", TestTree.File("late.bin", 7777))));
        await rescan;

        Assert.Equal(42, replacement.Size);
        Assert.Same(replacement, window.TreeSource!.Items.Single());
        Assert.Contains("discarded", window.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void RescanNode_NodeDetachedFromScanRoot_NoOp()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");
        FsTreeOps.RemoveChild(docs); // now a ghost

        var fake = new FakeScanner();
        window.ScanFunc = fake.Scan;
        var task = window.RescanNodeAsync(docs);

        // Deliberately not awaited: the guard must return synchronously. If it ever
        // regresses, this fails fast instead of hanging on the fake's completion.
        Assert.True(task.IsCompleted);
        Assert.Equal(0, fake.Calls);
    }

    // =====================================================================
    // Delete
    // =====================================================================

    [AvaloniaFact]
    public async Task DeleteNode_ThenDeleteFormerParent_RootSizeCorrect()
    {
        // The headline corruption: a removed node kept its Parent chain, so deleting
        // its former parent subtracted the child's size into the live tree twice.
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");
        var report = TestTree.Find(root, "report.pdf");

        FsTreeOps.RemoveChild(report);
        Assert.Equal(5000, docs.Size);
        Assert.Equal(8000, root.Size);

        FsTreeOps.RemoveChild(docs);
        Assert.Equal(3000, root.Size); // video.mp4 only — not 3000 - 4000
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task DeleteNode_ConfirmationDeclined_TreeUntouched()
    {
        var (window, root) = Host(confirmDeletes: false);
        var video = TestTree.Find(root, "video.mp4");

        await window.DeleteNodeAsync(video);

        Assert.Equal(12000, root.Size);
        Assert.Contains(root.Children!, c => ReferenceEquals(c, video));
    }

    [AvaloniaFact]
    public async Task DeleteNode_ConfirmationRequest_CarriesNamePathAndSize()
    {
        var root = SampleTree();
        MainWindow.DeleteRequest? seen = null;
        var window = new MainWindow
        {
            ConfirmDelete = req => { seen = req; return Task.FromResult(false); },
            ReportProblem = (_, _) => Task.CompletedTask,
        };
        window.Show();
        window.LoadTree(root);

        var video = TestTree.Find(root, "video.mp4");
        await window.DeleteNodeAsync(video);

        Assert.NotNull(seen);
        Assert.Equal("video.mp4", seen!.Value.Name);
        Assert.Equal(@"C:\scan\video.mp4", seen.Value.FullPath);
        Assert.Equal(3000, seen.Value.Size);
    }

    [AvaloniaFact]
    public void DeleteNode_DetachedNode_NoConfirmationNoMutation()
    {
        var (window, root) = Host();
        var video = TestTree.Find(root, "video.mp4");
        FsTreeOps.RemoveChild(video);
        long rootAfterRemoval = root.Size;

        bool asked = false;
        window.ConfirmDelete = _ => { asked = true; return Task.FromResult(true); };

        var task = window.DeleteNodeAsync(video); // ghost node from a stale context menu

        Assert.True(task.IsCompleted);
        Assert.False(asked);
        Assert.Equal(rootAfterRemoval, root.Size);
    }

    // =====================================================================
    // Busy state
    // =====================================================================

    [AvaloniaFact]
    public void GetContextMenuState_WhenBusy_Hidden()
    {
        var root = SampleTree();
        var state = MainWindow.GetContextMenuState(TestTree.Find(root, "docs"), isBusy: true);
        Assert.False(state.Show);
    }

    [AvaloniaFact]
    public async Task RescanNode_WhileAnotherRescanRuns_SecondIsIgnored()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");
        var archive = TestTree.Find(root, "archive");

        var fake = new FakeScanner();
        window.ScanFunc = fake.Scan;
        var first = window.RescanNodeAsync(docs);

        await window.RescanNodeAsync(archive); // must not start a second scan
        Assert.Equal(1, fake.Calls);

        fake.Completion.SetResult(TestTree.Seal(TestTree.Dir("docs", TestTree.File("f", 1))));
        await first;
    }

    [AvaloniaFact]
    public async Task RescanNode_ClearsContextNode()
    {
        var (window, root) = Host();
        var docs = TestTree.Find(root, "docs");

        var fake = new FakeScanner();
        window.ScanFunc = fake.Scan;
        var rescan = window.RescanNodeAsync(docs);
        fake.Completion.SetResult(TestTree.Seal(TestTree.Dir("docs", TestTree.File("f", 10))));
        await rescan;

        Assert.Null(window.ContextNode);
    }

    [AvaloniaFact]
    public async Task StartScan_Failure_KeepsPreviousSuccessfulPath()
    {
        var window = new MainWindow
        {
            ConfirmDelete = _ => Task.FromResult(false),
            ReportProblem = (_, _) => Task.CompletedTask,
            ScanFunc = (path, _, _) => Task.FromResult(TestTree.Seal(TestTree.Dir(path, TestTree.File("f", 1)))),
        };
        window.Show();

        await window.StartScanAsync(@"C:\good");
        Assert.Equal(@"C:\good", window.LastScanPath);

        window.ScanFunc = (_, _, _) => throw new IOException("drive vanished");
        await window.StartScanAsync(@"C:\bad");

        Assert.Equal(@"C:\good", window.LastScanPath);
        Assert.Contains("failed", window.StatusText, StringComparison.OrdinalIgnoreCase);
    }
}
