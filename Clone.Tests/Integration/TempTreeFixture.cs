namespace Clone.Tests.Integration;

/// <summary>
/// A real on-disk tree under %TEMP%\CloneTests_&lt;guid&gt; with known totals.
/// Only files created by this fixture are ever touched; Dispose removes the
/// fixture directory and nothing else.
/// </summary>
public sealed class TempTreeFixture : IDisposable
{
    public string Root { get; }

    public const int ExpectedFiles = 6;
    public const int ExpectedDirs = 23;              // empty + "sub with space" + deep + l01..l20
    public const long ExpectedBytes = 10_000 + 100 + 50 + 2_000 + 300 + 10;

    public TempTreeFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), $"CloneTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);

        WriteFile(Path.Combine(Root, "big.bin"), 10_000);
        WriteFile(Path.Combine(Root, "small.txt"), 100);

        string hidden = Path.Combine(Root, "hidden.sys");
        WriteFile(hidden, 50);
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);

        Directory.CreateDirectory(Path.Combine(Root, "empty"));

        string sub = Path.Combine(Root, "sub with space");
        Directory.CreateDirectory(sub);
        WriteFile(Path.Combine(sub, "file1.dat"), 2_000);
        WriteFile(Path.Combine(sub, "ünïcode 文件.txt"), 300);

        string deep = Path.Combine(Root, "deep");
        for (int i = 1; i <= 20; i++)
            deep = Path.Combine(deep, $"l{i:D2}");
        Directory.CreateDirectory(deep);
        WriteFile(Path.Combine(deep, "leaf.txt"), 10);
    }

    private static void WriteFile(string path, int size) =>
        File.WriteAllBytes(path, new byte[size]);

    public void Dispose()
    {
        try
        {
            ClearAttributes(new DirectoryInfo(Root));
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            Thread.Sleep(200);
            Directory.Delete(Root, recursive: true);
        }
    }

    private static void ClearAttributes(DirectoryInfo dir)
    {
        foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            f.Attributes = FileAttributes.Normal;
    }
}
