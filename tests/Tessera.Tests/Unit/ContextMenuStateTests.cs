using Tessera.Models;
using Tessera.UI;
using Xunit;

namespace Tessera.Tests.Unit;

/// <summary>
/// Which context-menu actions a node offers. GetContextMenuState is a pure static over an
/// FsNode, so these are unit tests — they previously sat in the headless layer and paid
/// for an Avalonia application none of them ever used.
/// </summary>
public class ContextMenuStateTests
{
    public static TheoryData<string, bool, bool, bool, bool, bool> CtxCases() => new()
    {
        // node kind,   busy,  Show,  CanDelete, CanRescan, CanTop
        { "null",       false, false, false,     false,     false },
        { "dir",        true,  false, false,     false,     false },
        { "root",       false, true,  false,     true,      true },
        { "dir",        false, true,  true,      true,      true },
        { "file",       false, true,  true,      false,     false },
        { "reparse",    false, true,  true,      false,     false },
    };

    [Theory]
    [MemberData(nameof(CtxCases))]
    public void GetContextMenuState_Matrix(string kind, bool busy,
        bool show, bool canDelete, bool canRescan, bool canTop)
    {
        var root = TestTree.Seal(
            TestTree.Dir(@"C:\scan",
                TestTree.Dir("dir", TestTree.File("f", 10)),
                TestTree.File("file", 5),
                TestTree.Reparse("reparse")));

        FsNode? node = kind switch
        {
            "null" => null,
            "root" => root,
            _ => TestTree.Find(root, kind),
        };

        var state = MainWindow.GetContextMenuState(node, busy);

        Assert.Equal(show, state.Show);
        Assert.Equal(canDelete, state.CanDelete);
        Assert.Equal(canRescan, state.CanRescan);
        Assert.Equal(canTop, state.CanTopFiles);
    }
}
