using Avalonia;
using Clone.Scanning;

namespace Clone;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Headless mode: "Clone --scan <path>" prints totals and exits (no window).
        if (args.Length >= 2 && args[0] == "--scan")
        {
            var progress = new ScanProgress();
            var root = Scanner.ScanAsync(args[1], progress, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"{root.GetFullPath()}");
            Console.WriteLine($"files={Volatile.Read(ref progress.Files)} dirs={Volatile.Read(ref progress.Dirs)} " +
                              $"bytes={root.Size} errors={Volatile.Read(ref progress.Errors)}");
            return;
        }

        // "Clone <path>" opens the window and immediately scans that path.
        if (args.Length >= 1 && Directory.Exists(args[0]))
            InitialPath = Path.GetFullPath(args[0]);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    internal static string? InitialPath;

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
