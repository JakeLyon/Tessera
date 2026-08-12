using Avalonia;
using Avalonia.Headless;
using Clone.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Clone.Tests;

/// <summary>
/// Headless Avalonia bootstrap for [AvaloniaFact] tests. Uses the real Clone.App so
/// the Fluent + TreeDataGrid themes load; the headless lifetime is not a classic
/// desktop lifetime, so App.OnFrameworkInitializationCompleted creates no MainWindow.
/// </summary>
public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
