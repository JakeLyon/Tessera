using System.Runtime.InteropServices;
using Avalonia;
using Clone.Scanning;
using Clone.UI;

namespace Clone;

internal static class Program
{
    /// <summary>Exit codes: 0 success, 1 startup failure, 2 usage error, 3 scan failure.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless mode: "Clone --scan <path>" prints totals and exits (no window).
        if (args.Length >= 1 && args[0] == "--scan")
        {
            AttachToParentConsole();
            int code = RunScanCli(args, Console.Out, Console.Error);
            Console.Out.Flush();
            Console.Error.Flush();
            return code;
        }

        // "Clone <path>" opens the window and immediately scans that path.
        if (args.Length >= 1 && Directory.Exists(args[0]))
            InitialPath = Path.GetFullPath(args[0]);

        // A task nobody awaited must not escalate to a process kill; the UI
        // deliberately fires several off (drive enumeration, dialog results).
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();

        // Last resort. The process is going down either way — this exists so it
        // does not go down in silence, which is what a startup crash looks like today.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashHandler.Report(owner: null, "Clone has to close", ex);
        };

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Failing before the window exists gives no owner to parent a dialog to,
            // and no status bar to fall back on.
            CrashHandler.Report(owner: null, "Clone could not start", ex);
            return 1;
        }
    }

    internal static int RunScanCli(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            stderr.WriteLine("usage: Clone --scan <path>");
            return 2;
        }

        try
        {
            var progress = new ScanProgress();
            var root = Scanner.ScanAsync(args[1], progress, CancellationToken.None).GetAwaiter().GetResult();
            stdout.WriteLine(root.GetFullPath());
            stdout.WriteLine($"files={Volatile.Read(ref progress.Files)} dirs={Volatile.Read(ref progress.Dirs)} " +
                             $"bytes={root.Size} errors={Volatile.Read(ref progress.Errors)}");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"scan failed: {ex.Message}");
            return 3;
        }
    }

    internal static string? InitialPath;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    private const uint AttachParentProcess = 0xFFFFFFFF;

    /// <summary>
    /// A WinExe has no console of its own, so Console.WriteLine goes nowhere when the
    /// user runs "Clone.exe --scan" from a terminal. Borrow the parent's console.
    /// No-ops when output is already redirected or there is no parent console
    /// (double-clicked), both of which already work.
    /// </summary>
    private static void AttachToParentConsole()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            if (!AttachConsole(AttachParentProcess))
                return;
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch (Exception)
        {
            // Attaching is best-effort; never fail the scan over console plumbing.
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
