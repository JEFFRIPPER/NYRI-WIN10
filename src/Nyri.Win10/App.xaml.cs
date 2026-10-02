using System.Windows;
using Nyri.Win10.Services;
using Nyri.Win10.Shell;

namespace Nyri.Win10;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private ShellRuntime? _runtime;
    private ShellCoordinator? _shell;
    private bool _revealRequested;
    private bool _exiting;
    public ThemeService? Theme { get; private set; }
    public ShellCoordinator? Shell => _shell;

    protected override async void OnStartup(StartupEventArgs e)
    {
        AppDiagnostics.Write("Startup requested");
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimary)
        {
            AppDiagnostics.Write("Existing instance signalled: " + _singleInstance.SignalPrimary());
            Shutdown();
            return;
        }
        _singleInstance.StartListening(() =>
        {
            if (Dispatcher.HasShutdownStarted) return;
            try
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_shell is not null) _shell.Reveal();
                    else _revealRequested = true;
                });
            }
            catch (InvalidOperationException) { }
        });
        DispatcherUnhandledException += (_, args) => AppDiagnostics.Write("Unhandled UI error: " + args.Exception);
        base.OnStartup(e);
        Theme = new ThemeService(Resources);
        _runtime = new ShellRuntime();
        var saved = _runtime.Settings.Load();
        Theme.Apply(saved.Dark, saved.Palette);
        _shell = new ShellCoordinator(_runtime, Theme);
        MainWindow = _shell.Bar;
        _shell.Bar.Closed += (_, _) => { if (!_exiting && !Dispatcher.HasShutdownStarted) Shutdown(); };
        await _shell.StartAsync();
        if (_revealRequested) { _revealRequested = false; _shell.Reveal(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        _shell?.Dispose();
        _runtime?.Dispose();
        _singleInstance?.Dispose();
        AppDiagnostics.Write("Exit: " + e.ApplicationExitCode);
        base.OnExit(e);
    }
}
