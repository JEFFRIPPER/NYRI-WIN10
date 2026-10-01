using System.Threading;
using System.Windows;

namespace Nyri.Win10;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = @"Local\JEFFRIPPER.NYRI-WIN10";
        _singleInstanceMutex = new Mutex(true, mutexName, out _ownsMutex);

        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
            _singleInstanceMutex?.ReleaseMutex();

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}