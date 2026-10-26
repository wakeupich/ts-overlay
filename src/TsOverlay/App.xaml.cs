using System.Threading;
using System.Windows;
using TsOverlay.Services;

namespace TsOverlay;

public partial class App : Application
{
    private Mutex? _mutex;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase)))
        {
            Probe.Run(e.Args);
            Shutdown(0);
            return;
        }

        _mutex = new Mutex(true, @"Local\TsOverlay.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // Второй экземпляр не нужен: приложение уже живёт в трее.
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Необработанное исключение", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Критическая ошибка", args.ExceptionObject as Exception);

        _host = new AppHost();
        _host.Start();

        if (e.Args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
        {
            _host.ShowSettings();
        }

        if (e.Args.Any(a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase)))
        {
            _host.ShowPreviewNow();
        }

        if (e.Args.Any(a => a.Equals("--hud", StringComparison.OrdinalIgnoreCase)))
        {
            _host.ShowHudNow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error("Ошибка при завершении", ex);
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }
}
