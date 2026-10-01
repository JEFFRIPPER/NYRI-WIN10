using System.Windows;
using Nyri.Win10.Services;

namespace Nyri.Win10;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private bool _revealRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDiagnostics.Write("Startup requested");
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            AppDiagnostics.Write("Existing instance signalled: " + _singleInstance.SignalPrimary());
            Shutdown();
            return;
        }

        _singleInstance.StartListening(() => Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is Nyri.Win10.MainWindow island) island.Reveal();
        }));
        DispatcherUnhandledException += (_, args) => AppDiagnostics.Write("Unhandled UI error: " + args.Exception);
        base.OnStartup(e);
    }

    internal void WindowReady(Nyri.Win10.MainWindow island)
    {
        if (!_revealRequested) return;
        _revealRequested = false;
        island.Reveal();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        AppDiagnostics.Write("Exit: " + e.ApplicationExitCode);
        base.OnExit(e);
    }
}
