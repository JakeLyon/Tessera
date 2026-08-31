using System.Diagnostics;
using Tessera.Util;
using Xunit;

namespace Tessera.Tests.Integration;

/// <summary>
/// The ShellOps paths that actually leave the process. These live here rather than beside
/// the pure argv tests in Unit/ because they start real processes — one of them can put a
/// window on screen — and a unit test should do neither.
/// </summary>
public class ShellOpsProcessTests
{
    [Fact]
    public void RunAndWait_MissingHelperBinary_ReturnsFailureInsteadOfThrowing()
    {
        // The bug: Process.Start throws Win32Exception when gio/xdg-open/osascript
        // is absent, and the exception escaped an async void handler, killing the app.
        var psi = new ProcessStartInfo($"tessera-nonexistent-helper-{Guid.NewGuid():N}")
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
}
