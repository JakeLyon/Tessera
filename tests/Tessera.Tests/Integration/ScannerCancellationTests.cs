using Tessera.Models;
using Tessera.Scanning;
using Xunit;

namespace Tessera.Tests.Integration;

public sealed class ScannerCancellationTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _root;

    public ScannerCancellationTests()
    {
        _root = _temp.Path;
        // 30×30 = 900 dirs with a small file each — enough work to observe a mid-scan cancel.
        for (int i = 0; i < 30; i++)
        {
            for (int j = 0; j < 30; j++)
            {
                string dir = Path.Combine(_root, $"d{i:D2}", $"s{j:D2}");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "f.bin"), new byte[16]);
            }
        }
    }

    public void Dispose() => _temp.Dispose();

    private static void AssertWellFormed(FsNode node)
    {
        if (node.Children is not { } children)
            return;
        long sum = 0;
        foreach (var c in children)
        {
            Assert.Same(node, c.Parent);
            AssertWellFormed(c);
            sum += c.Size;
        }
        // Aggregate always runs after cancellation, so any dir WITH a children array
        // must be internally consistent.
        Assert.Equal(sum, node.Size);
    }

    [WindowsFact]
    public void PreCancelledToken_ReturnsWithoutHang()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var task = Scanner.ScanAsync(_root, new ScanProgress(), cts.Token);
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "scan did not return promptly");

        // Workers exit before processing anything: the root may have no children at all.
        var root = task.Result;
        Assert.NotNull(root);
        AssertWellFormed(root);
    }

    [WindowsFact]
    public void CancelMidScan_PartialTreeWellFormed()
    {
        using var cts = new CancellationTokenSource();
        var progress = new ScanProgress();
        var task = Scanner.ScanAsync(_root, progress, cts.Token);

        // Cancel once some directories have been processed. If the scan is so fast it
        // finishes first, the assertions below still hold for the complete tree.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Volatile.Read(ref progress.Dirs) < 50 && !task.IsCompleted && sw.ElapsedMilliseconds < 5000)
            Thread.SpinWait(100);
        cts.Cancel();

        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "cancelled scan did not return promptly");
        AssertWellFormed(task.Result);
    }
}
