using Tessera.Models;
using Tessera.Scanning;
using Xunit;

namespace Tessera.Tests.Integration;

public class ScannerBasicTests : IClassFixture<TempTreeFixture>
{
    private readonly TempTreeFixture _fx;

    public ScannerBasicTests(TempTreeFixture fx) => _fx = fx;

    private (FsNode Root, ScanProgress Progress) Scan(string? path = null)
    {
        var progress = new ScanProgress();
        var root = Scanner.ScanAsync(path ?? _fx.Root, progress, CancellationToken.None)
            .GetAwaiter().GetResult();
        return (root, progress);
    }

    private static IEnumerable<FsNode> Walk(FsNode node)
    {
        yield return node;
        if (node.Children is { } children)
            foreach (var c in children)
                foreach (var n in Walk(c))
                    yield return n;
    }

    [WindowsFact]
    public void Scan_ExactCounts()
    {
        var (root, progress) = Scan();

        Assert.Equal(TempTreeFixture.ExpectedFiles, Volatile.Read(ref progress.Files));
        Assert.Equal(TempTreeFixture.ExpectedDirs, Volatile.Read(ref progress.Dirs));
        Assert.Equal(TempTreeFixture.ExpectedBytes, Volatile.Read(ref progress.Bytes));
        Assert.Equal(0, Volatile.Read(ref progress.Errors));
        Assert.Equal(TempTreeFixture.ExpectedBytes, root.Size);
    }

    [WindowsFact]
    public void Scan_EveryDirSize_EqualsSumOfChildren()
    {
        var (root, _) = Scan();
        foreach (var dir in Walk(root).Where(n => n.Children is { Length: > 0 }))
            Assert.Equal(dir.Children!.Sum(c => c.Size), dir.Size);
    }

    [WindowsFact]
    public void Scan_AllChildArraysSortedDesc()
    {
        var (root, _) = Scan();
        foreach (var dir in Walk(root).Where(n => n.Children is { Length: > 1 }))
            for (int i = 1; i < dir.Children!.Length; i++)
                Assert.True(dir.Children[i - 1].Size >= dir.Children[i].Size);
    }

    [WindowsFact]
    public void Scan_HiddenSystemFileIncluded()
    {
        var (root, _) = Scan();
        var hidden = root.Children!.FirstOrDefault(c => c.Name == "hidden.sys");
        Assert.NotNull(hidden);
        Assert.Equal(50, hidden!.Size);
    }

    [WindowsFact]
    public void Scan_EmptyDir_EmptyArrayNotNull_SizeZero()
    {
        var (root, _) = Scan();
        var empty = root.Children!.First(c => c.Name == "empty");
        Assert.True(empty.IsDir);
        Assert.NotNull(empty.Children);
        Assert.Empty(empty.Children!);
        Assert.Equal(0, empty.Size);
    }

    [WindowsFact]
    public void Scan_UnicodeAndSpaceNames_RoundTripToRealPaths()
    {
        var (root, _) = Scan();
        foreach (var file in Walk(root).Where(n => !n.IsDir))
            Assert.True(File.Exists(file.GetFullPath()), $"missing: {file.GetFullPath()}");
    }

    [WindowsFact]
    public void Scan_DeepNesting_LeafFoundAtDepth20()
    {
        var (root, _) = Scan();
        var leaf = Walk(root).FirstOrDefault(n => n.Name == "leaf.txt");
        Assert.NotNull(leaf);
        Assert.Equal(10, leaf!.Size);

        int depth = 0;
        for (var n = leaf; n.Parent is not null; n = n.Parent)
            depth++;
        Assert.Equal(22, depth); // root/deep/l01..l20/leaf.txt
    }

    [WindowsFact]
    public void Scan_TrailingSeparator_Normalized()
    {
        var (root, _) = Scan(_fx.Root + Path.DirectorySeparatorChar);
        Assert.Equal(Path.GetFullPath(_fx.Root), root.Name);
        Assert.Equal(TempTreeFixture.ExpectedBytes, root.Size);
    }
}
