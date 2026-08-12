using System.Diagnostics;
using Tessera;
using Xunit;

namespace Tessera.Tests.Integration;

public class CliTests : IClassFixture<TempTreeFixture>
{
    private readonly TempTreeFixture _fx;

    public CliTests(TempTreeFixture fx) => _fx = fx;

    // ---- In-process: argument handling and exit codes ----

    [WindowsFact]
    public void RunScanCli_PrintsTotals_ReturnsZero()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int code = Program.RunScanCli(["--scan", _fx.Root], stdout, stderr);

        string output = stdout.ToString();
        Assert.Equal(0, code);
        Assert.Contains(Path.GetFullPath(_fx.Root), output);
        Assert.Contains($"files={TempTreeFixture.ExpectedFiles}", output);
        Assert.Contains($"dirs={TempTreeFixture.ExpectedDirs}", output);
        Assert.Contains($"bytes={TempTreeFixture.ExpectedBytes}", output);
        Assert.Contains("errors=0", output);
        Assert.Empty(stderr.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RunScanCli_MissingPath_ReturnsTwoAndWritesUsageToStderr(string? path)
    {
        string[] args = path is null ? ["--scan"] : ["--scan", path];
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Previously this fell through and silently opened the GUI.
        int code = Program.RunScanCli(args, stdout, stderr);

        Assert.Equal(2, code);
        Assert.Contains("usage", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(stdout.ToString());
    }

    [Fact]
    public void RunScanCli_UnscannablePath_ReturnsThree()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Embedded NUL makes Path.GetFullPath throw; the scanner swallows the
        // IO/access exceptions that merely mark a directory inaccessible.
        int code = Program.RunScanCli(["--scan", "bad\0path"], stdout, stderr);

        Assert.Equal(3, code);
        Assert.Contains("scan failed", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ---- Out-of-process: the real executable ----

    /// <remarks>
    /// Redirecting stdout hands the child a valid pipe handle, so this proves the exe
    /// runs headlessly, writes totals and returns the right exit code. It cannot
    /// reproduce the no-console-attached case that AttachConsole fixes — running from
    /// an interactive terminal stays a manual check.
    /// </remarks>
    private static (int ExitCode, string StdOut, string StdErr) RunExe(params string[] args)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Tessera.exe");
        Assert.True(File.Exists(exe), $"Tessera.exe not found next to the tests at {exe}");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "the CLI did not exit within 60s (did it open a window?)");
        return (process.ExitCode, stdout, stderr);
    }

    [WindowsFact]
    public void Exe_ScanMode_EndToEnd_WritesTotalsToStdoutAndExitsZero()
    {
        var (exitCode, stdout, _) = RunExe("--scan", _fx.Root);

        Assert.Equal(0, exitCode);
        Assert.Contains($"bytes={TempTreeFixture.ExpectedBytes}", stdout);
        Assert.Contains($"files={TempTreeFixture.ExpectedFiles}", stdout);
    }

    [WindowsFact]
    public void Exe_ScanMode_NoPath_ExitsTwoWithUsage()
    {
        var (exitCode, stdout, stderr) = RunExe("--scan");

        Assert.Equal(2, exitCode);
        Assert.Contains("usage", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(stdout);
    }
}
