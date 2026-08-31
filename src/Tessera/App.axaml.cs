using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Tessera.UI;

namespace Tessera;

internal class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // The UI is driven by async void event handlers, where a throw is
            // unhandled by definition. Mark it handled: a failed menu action should
            // report itself, not take the window down with it.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                CrashHandler.Report(window, "Something went wrong", e.Exception);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
