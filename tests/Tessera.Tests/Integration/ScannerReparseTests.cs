using System.Diagnostics;
using Tessera.Models;
using Tessera.Scanning;
using Xunit;

namespace Tessera.Tests.Integration;

/// <summary>
/// Junction handling. Junctions are created with "cmd /c mklink /J" (no admin or
/// developer mode required, unlike file symlinks) inside fixture-owned temp dirs.
/// </summary>
public sealed class ScannerReparseTests : IDisposable
{
    private readonly string _root;

    public ScannerReparseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"TesseraTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Junction entries must be removed non-recursively first — recursive delete
        // refuses them. Walk manually so we never descend through a junction (the
        // cycle test creates one pointing back up the tree).
        var stack = new Stack<string>();
        stack.Push(_root);
        var junctions = new List<string>();
        while (stack.Count > 0)
        {
            foreach (var sub in Directory.EnumerateDirectories(stack.Pop()))
            {
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                    junctions.Add(sub);
                else
                    stack.Push(sub);
            }
        }
        foreach (var junction in junctions)
            Directory.Delete(junction, recursive: false);
        Directory.Delete(_root, recursive: true);
    }

    private static void MakeJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    private static FsNode Scan(string path, ScanProgress? progress = null)
    {
        var task = Scanner.ScanAsync(path, progress ?? new ScanProgress(), CancellationToken.None);
        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "scan did not complete within 30s");
        return task.Result;
    }

    [WindowsFact]
    public void Junction_IsReparseLeaf_ZeroSize_NeverDescended()
    {
        string target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "payload.bin"), new byte[5000]);

        string scanRoot = Path.Combine(_root, "scanme");
        Directory.CreateDirectory(scanRoot);
        MakeJunction(Path.Combine(scanRoot, "link"), target);

        var root = Scan(scanRoot);

        var link = root.Children!.Single(c => c.Name == "link");
        Assert.True(link.IsDir);
        Assert.True(link.IsReparse);
        Assert.Equal(0, link.Size);
        Assert.Null(link.Children); // never enqueued, never enumerated
        Assert.Equal(0, root.Size); // payload lives outside the scanned root
    }

    [WindowsFact]
    public void Junction_TargetInsideRoot_NoDoubleCount()
    {
        string scanRoot = Path.Combine(_root, "scanme");
        string target = Path.Combine(scanRoot, "real");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "payload.bin"), new byte[5000]);
        MakeJunction(Path.Combine(scanRoot, "mirror"), target);

        var root = Scan(scanRoot);

        Assert.Equal(5000, root.Size); // counted once, at the real location
        Assert.Equal(5000, root.Children!.Single(c => c.Name == "real").Size);
        Assert.Equal(0, root.Children!.Single(c => c.Name == "mirror").Size);
    }

    [WindowsFact]
    public void Junction_CycleToAncestor_CompletesWithoutHang()
    {
        string scanRoot = Path.Combine(_root, "scanme");
        string deep = Path.Combine(scanRoot, "a", "b");
        Directory.CreateDirectory(deep);
        File.WriteAllBytes(Path.Combine(deep, "f.bin"), new byte[100]);
        // Junction deep in the tree pointing back at the scan root — a cycle if followed.
        MakeJunction(Path.Combine(deep, "loop"), scanRoot);

        var progress = new ScanProgress();
        var root = Scan(scanRoot, progress);

        Assert.Equal(100, root.Size);
        Assert.Equal(1, Volatile.Read(ref progress.Files));
    }

    [WindowsFact]
    public void Junction_TargetOutsideRoot_NotFollowed()
    {
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "huge.bin"), new byte[9000]);

        string scanRoot = Path.Combine(_root, "scanme");
        Directory.CreateDirectory(scanRoot);
        File.WriteAllBytes(Path.Combine(scanRoot, "own.bin"), new byte[10]);
        MakeJunction(Path.Combine(scanRoot, "external"), outside);

        var root = Scan(scanRoot);

        Assert.Equal(10, root.Size); // outside bytes never counted
    }
}
