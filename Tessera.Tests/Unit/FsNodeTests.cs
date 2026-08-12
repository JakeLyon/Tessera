using Tessera.Models;
using Xunit;

namespace Tessera.Tests.Unit;

public class FsNodeTests
{
    [Theory]
    [InlineData(@"C:\data")]
    [InlineData(@"C:\")]
    public void GetFullPath_RootOnly_ReturnsNameVerbatim(string rootName)
        => Assert.Equal(rootName, new FsNode(rootName, FsNode.FlagDir).GetFullPath());

    [Fact]
    public void GetFullPath_DriveRootChild_NoDoubleSeparator()
    {
        var root = TestTree.Seal(TestTree.Dir(@"C:\", TestTree.File("foo.txt", 1)));
        Assert.Equal(@"C:\foo.txt", root.Children![0].GetFullPath());
    }

    [Fact]
    public void GetFullPath_Nested_ThreeLevels()
    {
        var root = TestTree.Seal(
            TestTree.Dir(@"D:\scan",
                TestTree.Dir("a",
                    TestTree.Dir("b",
                        TestTree.File("c.bin", 5)))));
        var leaf = TestTree.Find(root, "c.bin");
        Assert.Equal(@"D:\scan\a\b\c.bin", leaf.GetFullPath());
    }

    [Fact]
    public void PercentOfParent_NoParent_IsOne()
        => Assert.Equal(1.0, new FsNode("x") { Size = 42 }.PercentOfParent);

    [Fact]
    public void PercentOfParent_ZeroSizeParent_IsOne()
    {
        var root = TestTree.Seal(TestTree.Dir("root", TestTree.File("empty.txt", 0)));
        Assert.Equal(1.0, root.Children![0].PercentOfParent);
    }

    [Fact]
    public void PercentOfParent_Half()
    {
        var root = TestTree.Seal(TestTree.Dir("root",
            TestTree.File("a", 50), TestTree.File("b", 50)));
        Assert.Equal(0.5, root.Children![0].PercentOfParent);
    }

    [Fact]
    public void Extension_Directory_IsEmpty()
        => Assert.Equal("", TestTree.Dir("folder.name").Extension);

    [Theory]
    [InlineData("FILE.TXT", ".txt")]
    [InlineData("archive.tar.GZ", ".gz")]
    [InlineData("noext", "")]
    [InlineData(".gitignore", ".gitignore")] // Path.GetExtension treats the whole name as extension
    public void Extension_Files(string name, string expected)
        => Assert.Equal(expected, TestTree.File(name, 1).Extension);

    [Theory]
    [InlineData(FsNode.FlagDir, true, false, false)]
    [InlineData(FsNode.FlagDir | FsNode.FlagReparse, true, true, false)]
    [InlineData(FsNode.FlagDir | FsNode.FlagAccessDenied, true, false, true)]
    [InlineData((byte)0, false, false, false)]
    public void Flags_Combinations(byte flags, bool isDir, bool isReparse, bool isDenied)
    {
        var n = new FsNode("n", flags);
        Assert.Equal(isDir, n.IsDir);
        Assert.Equal(isReparse, n.IsReparse);
        Assert.Equal(isDenied, n.IsAccessDenied);
    }
}
