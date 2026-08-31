using Tessera.Models;
using Tessera.Scanning;
using Xunit;

namespace Tessera.Tests.Integration;

/// <summary>
/// The scanner's workers share a pending-directory counter; a worker that fails
/// without decrementing it strands every other worker in a spin loop and the scan
/// never completes. Real filesystems produce those failures too rarely to test
/// against, so they are injected through <c>Scanner.OnDirectoryEnter</c>.
/// </summary>
public class ScannerFailureTests : IDisposable
{
    private readonly TempDir _temp = new("TesseraFail");
    private readonly string _root;

    // Sizes of the tree built below.
    private const long TopFileBytes = 1_000;
    private const long BranchBytes = 500;
    private const long DeepBytes = 40;
    private const long TotalBytes = TopFileBytes + BranchBytes + DeepBytes;

    public ScannerFailureTests()
    {
        _root = _temp.Path;
        File.WriteAllBytes(Path.Combine(_root, "top.bin"), new byte[TopFileBytes]);

        string branch = Path.Combine(_root, "branch");
        Directory.CreateDirectory(branch);
        File.WriteAllBytes(Path.Combine(branch, "inside.bin"), new byte[BranchBytes]);

        // A chain, so a failure at the top of it leaves descendants that must never
        // be enqueued while the counter still has to drain to zero.
        string deep = Path.Combine(_root, "chain", "a", "b", "c");
        Directory.CreateDirectory(deep);
        File.WriteAllBytes(Path.Combine(deep, "leaf.bin"), new byte[DeepBytes]);
    }

    public void Dispose()
    {
        // The seam is static and other test classes scan in parallel: always clear it.
        Scanner.OnDirectoryEnter = null;
        _temp.Dispose();
    }

    /// <summary>
    /// Install a failure, scoped to this test's own tree — the seam is process-wide, so an
    /// unscoped predicate would poison scans running concurrently in other classes.
    /// </summary>
    private void FailUnder(string relative, Exception ex)
    {
        string target = relative.Length == 0 ? _root : Path.Combine(_root, relative);
        Scanner.OnDirectoryEnter = path =>
        {
            if (path.StartsWith(target, StringComparison.OrdinalIgnoreCase))
                throw ex;
        };
    }

    /// <summary>Scan with a hard timeout — the failure mode under test is a hang, not an exception.</summary>
    private FsNode ScanWithin(ScanProgress progress, int timeoutMs = 30_000)
    {
        var task = Scanner.ScanAsync(_root, progress, CancellationToken.None);
        Assert.True(task.Wait(timeoutMs), $"the scan did not complete within {timeoutMs} ms — workers stranded");
        return task.Result;
    }

    [WindowsFact]
    public void RecoverableFailureInOneDirectory_ScanStillCompletes_AndIsCounted()
    {
        FailUnder("branch", new ArgumentException("injected: unusable directory name"));

        var progress = new ScanProgress();
        var root = ScanWithin(progress);

        var node = Array.Find(root.Children!, c => c.Name == "branch");
        Assert.NotNull(node);
        Assert.True(node!.IsScanError);
        Assert.False(node.IsAccessDenied);       // not a permissions problem — don't say it was
        Assert.Equal(1, Volatile.Read(ref progress.Errors));

        // Only the failing directory's contents are missing; the rest is intact.
        Assert.Equal(0, node.Size);
        Assert.Equal(TotalBytes - BranchBytes, root.Size);
        Assert.Contains(root.Children!, c => c.Name == "top.bin" && c.Size == TopFileBytes);
    }

    [WindowsFact]
    public void FailureInEveryDirectory_ScanStillCompletes()
    {
        // Worst case for the pending counter: every worker fails on every directory.
        FailUnder("", new ArgumentException("injected"));

        var progress = new ScanProgress();
        var root = ScanWithin(progress);

        Assert.True(root.IsScanError);
        Assert.Equal(0, root.Size);
        Assert.Empty(root.Children!);
    }

    [WindowsFact]
    public void FailureAtTopOfAChain_DoesNotStrandTheDirectoriesBelowIt()
    {
        FailUnder(Path.Combine("chain", "a"), new NotSupportedException("injected"));

        var progress = new ScanProgress();
        var root = ScanWithin(progress);

        Assert.Equal(1, Volatile.Read(ref progress.Errors));
        Assert.Equal(TotalBytes - DeepBytes, root.Size);   // only leaf.bin is lost
    }

    [WindowsFact]
    public void UnrecoverableFailure_SurfacesUnwrapped_NotAsAggregateException()
    {
        // Outside the recoverable set, so it propagates. The point is that it arrives
        // as itself rather than as "One or more errors occurred."
        FailUnder("", new OutOfMemoryException("injected"));

        var task = Scanner.ScanAsync(_root, new ScanProgress(), CancellationToken.None);

        var ex = Assert.Throws<AggregateException>(() => task.Wait(30_000));
        var inner = Assert.Single(ex.Flatten().InnerExceptions);
        Assert.IsType<OutOfMemoryException>(inner);
        Assert.Equal("injected", inner.Message);
    }
}
