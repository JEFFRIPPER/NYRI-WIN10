using System.Windows;
using System.Windows.Controls;
using Nyri.Win10.Models;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

public partial class DockWindow : Window
{
    private readonly ShellCoordinator _shell;
    public DockWindow(ShellCoordinator shell)
    {
        _shell = shell;
        InitializeComponent();
        _shell.Windows.Changed += Windows_Changed;
        Closed += (_, _) => _shell.Windows.Changed -= Windows_Changed;
    }
    public void Refresh()
    {
        PinnedList.ItemsSource = _shell.PinnedApps;
        RunningList.ItemsSource = _shell.Windows.Windows.Take(10).ToArray();
        Position();
    }
    private void Windows_Changed(object? sender, EventArgs e) => Refresh();
    private void Window_Loaded(object sender, RoutedEventArgs e) => Refresh();
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => Position();
    private void Position()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + Math.Max(0, (work.Width - ActualWidth) / 2);
        Top = work.Bottom - ActualHeight - 8;
        MaxWidth = Math.Max(320, work.Width - 24);
    }
    private void Search_Click(object sender, RoutedEventArgs e) => _shell.Router.Toggle(ShellPanel.Launcher);
    private void Pinned_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is LaunchableApp app && !_shell.Catalog.Launch(app, out var error)) _shell.ReportError(error ?? "Приложение не запущено");
    }
    private void Running_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is TrackedWindow window) _shell.Windows.Activate(window);
    }
    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is LaunchableApp app) _shell.TogglePin(app);
    }
}
