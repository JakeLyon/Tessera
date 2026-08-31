using Tessera.Util;
using Xunit;

namespace Tessera.Tests.Unit;

/// <summary>
/// The pure parts of ShellOps: argument construction and the failure paths that never
/// reach a process. Anything that actually starts one lives in Integration.
/// </summary>
public class ShellOpsTests
{
    // ---- AppleScript injection (the path must never become script) ----

    [Fact]
    public void BuildMacTrashArgs_PathIsTheFinalArgv_NotPartOfTheScript()
    {
        const string path = "/Users/me/Movies/holiday.mp4";
        var args = ShellOps.BuildMacTrashArgs(path);

        Assert.Equal(path, args[^1]);
        // Every script fragment is a constant; none embeds the path.
        foreach (var fragment in args[..^1])
            Assert.DoesNotContain("holiday", fragment);
        Assert.Contains(args, a => a.Contains("item 1 of argv"));
    }

    [Theory]
    [InlineData("/tmp/ab\"cd.txt")]
    [InlineData("/tmp/x\" & (do shell script \"rm -rf ~\") & \"")]
    [InlineData(@"/tmp/back\slash\path")]
    [InlineData("/tmp/new\nline")]
    public void BuildMacTrashArgs_HostileFilename_PassedThroughVerbatim(string path)
    {
        // Interpolating any of these into the AppleScript source would either break
        // the delete or execute the payload; as argv they are inert data.
        var args = ShellOps.BuildMacTrashArgs(path);

        Assert.Equal(path, args[^1]);
        Assert.DoesNotContain(args[..^1], fragment => fragment.Contains("rm -rf"));
    }

    // ---- Failure reporting instead of silence or exceptions ----

    [WindowsFact]
    public void DeleteToRecycleBin_PathAtOrOverMaxPath_ReportsLengthInsteadOfFailingSilently()
    {
        string longPath = @"C:\" + string.Join('\\', Enumerable.Repeat(new string('a', 30), 12));
        Assert.True(longPath.Length >= 260, "test path must exceed MAX_PATH");

        var result = ShellOps.DeleteToRecycleBin(longPath);

        Assert.False(result.Ok);
        Assert.Contains("260", result.Error);
        Assert.Contains(longPath.Length.ToString(), result.Error!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DeleteToRecycleBin_EmptyPath_FailsCleanly(string path)
    {
        var result = ShellOps.DeleteToRecycleBin(path);
        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void RevealInFileManager_EmptyPath_FailsCleanly()
    {
        var result = ShellOps.RevealInFileManager("  ", isDirectory: true);
        Assert.False(result.Ok);
    }
}
