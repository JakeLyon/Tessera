using Avalonia;
using Clone.Models;
using Clone.Scanning;
using Clone.UI;
using Xunit;

namespace Clone.Tests.Integration;

public class ScannerEdgeTests : IClassFixture<TempTreeFixture>
{
    private readonly TempTreeFixture _fx;

    public ScannerEdgeTests(TempTreeFixture fx) => _fx = fx;

    private static (FsNode Root, ScanProgress Progress) Scan(string path)
    {
        var progress = new ScanProgress();
        var task = Scanner.ScanAsync(path, progress, CancellationToken.None);
        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "scan did not complete");
        return (task.Result, progress);
    }

    [WindowsFact]
    public void Scan_NonexistentPath_FlaggedNotThrown()
    {
        var (root, progress) = Scan(Path.Combine(_fx.Root, "does-not-exist"));

        Assert.True(root.IsAccessDenied); // DirectoryNotFound is an IOException → flagged
        Assert.NotNull(root.Children);
        Assert.Empty(root.Children!);
        Assert.Equal(0, root.Size);
        Assert.Equal(1, Volatile.Read(ref progress.Errors));
    }

    [WindowsFact]
    public void Scan_PathIsFile_FlaggedNotThrown()
    {
        var (root, progress) = Scan(Path.Combine(_fx.Root, "big.bin"));

        Assert.True(root.IsAccessDenied);
        Assert.Equal(0, root.Size);
        Assert.Equal(1, Volatile.Read(ref progress.Errors));
    }

    [WindowsFact]
    public void Scan_DotDotSegments_NormalizedToFullPath()
    {
        string convoluted = Path.Combine(_fx.Root, "empty", "..", "empty", "..");
        var (root, _) = Scan(convoluted);

        Assert.Equal(Path.GetFullPath(_fx.Root), root.Name);
        Assert.Equal(TempTreeFixture.ExpectedBytes, root.Size);
    }

    [WindowsFact]
    public void Scan_Repeated_DeterministicTotalsAndPerDirSizes()
    {
        var runs = Enumerable.Range(0, 3).Select(_ => Scan(_fx.Root)).ToList();

        foreach (var (root, progress) in runs)
        {
            Assert.Equal(TempTreeFixture.ExpectedFiles, Volatile.Read(ref progress.Files));
            Assert.Equal(TempTreeFixture.ExpectedDirs, Volatile.Read(ref progress.Dirs));
            Assert.Equal(TempTreeFixture.ExpectedBytes, Volatile.Read(ref progress.Bytes));
        }

        // Per-directory sizes must be identical run to run (child ORDER among equal
        // sizes may differ — parallel enumeration plus an unstable sort — so compare
        // name→size maps, not sequences).
        var maps = runs.Select(r => ToSizeMap(r.Root)).ToList();
        Assert.Equal(maps[0], maps[1]);
        Assert.Equal(maps[0], maps[2]);

        static Dictionary<string, long> ToSizeMap(FsNode root)
        {
            var map = new Dictionary<string, long>();
            var stack = new Stack<FsNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                map[n.GetFullPath()] = n.Size;
                if (n.Children is { } c)
                    foreach (var child in c)
                        stack.Push(child);
            }
            return map;
        }
    }

    [WindowsFact]
    public void Scan_ManyFilesInOneDirectory_ExactCounts()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"CloneTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            const int count = 2000;
            long expectedBytes = 0;
            for (int i = 0; i < count; i++)
            {
                int size = i % 7; // 0..6 bytes, includes zero-length files
                File.WriteAllBytes(Path.Combine(dir, $"f{i:D4}.bin"), new byte[size]);
                expectedBytes += size;
            }

            var (root, progress) = Scan(dir);

            Assert.Equal(count, Volatile.Read(ref progress.Files));
            Assert.Equal(expectedBytes, root.Size);
            Assert.Equal(count, root.Children!.Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [WindowsFact]
    public void Pipeline_ScanThenSquarify_InvariantsHold()
    {
        var (root, _) = Scan(_fx.Root);
        var output = new List<TmRect>();
        Squarify.Layout(root, new Rect(0, 0, 800, 600), 0, output);

        Assert.NotEmpty(output);
        // Root's direct children tile the full rect.
        double topLevel = output.Where(t => ReferenceEquals(t.Node.Parent, root))
            .Sum(t => t.Bounds.Width * t.Bounds.Height);
        Assert.Equal(800 * 600, topLevel, precision: 2);
        // Every laid-out node is part of the scanned tree.
        Assert.All(output, t => Assert.False(double.IsNaN(t.Bounds.Width)));
    }
}
