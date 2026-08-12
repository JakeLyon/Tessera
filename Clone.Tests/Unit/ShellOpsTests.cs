using System.Diagnostics;
using Clone.Util;
using Xunit;

namespace Clone.Tests.Unit;

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
    public void RunAndWait_MissingHelperBinary_ReturnsFailureInsteadOfThrowing()
    {
        // The bug: Process.Start throws Win32Exception when gio/xdg-open/osascript
        // is absent, and the exception escaped an async void handler, killing the app.
        var psi = new ProcessStartInfo($"clone-nonexistent-helper-{Guid.NewGuid():N}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var result = ShellOps.RunAndWait(psi, "test-helper");

        Assert.False(result.Ok);
        Assert.Contains("not available", result.Error);
    }

    [WindowsFact]
    public void RevealInFileManager_NonexistentPath_DoesNotThrow()
    {
        // Explorer may well launch and show an error of its own, so Ok is not asserted
        // either way; the contract here is only that the call returns rather than
        // throwing into a UI event handler.
        var ex = Record.Exception(
            () => ShellOps.RevealInFileManager(@"C:\this\does\not\exist\file.txt", isDirectory: false));

        Assert.Null(ex);
    }

    [Fact]
    public void RevealInFileManager_EmptyPath_FailsCleanly()
    {
        var result = ShellOps.RevealInFileManager("  ", isDirectory: true);
        Assert.False(result.Ok);
    }
}
