using System.Windows;
using System.Windows.Threading;
using WinDeutsch.App.Platform;
using WinDeutsch.Core;

namespace WinDeutsch.App;

public partial class App : Application
{
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            // The first instance opens its library.
            Shutdown();
            return;
        }

        try
        {
            var controller = new AppController();
            controller.Start();
            _instance.Listen(() => Dispatcher.BeginInvoke(() => controller.OpenLibrary()));
        }
        catch (Exception ex)
        {
            Log.Error("Startup failed", ex);
            MessageBox.Show($"Win Deutsch не удалось запустить: {ex.Message}\nПодробности — в {AppPaths.LogFile}",
                "Win Deutsch", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    // One broken phrase or window must not take the app down: log and keep running.
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }
}
