using System.Windows;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

public partial class SettingsWindow : Window
{
    private readonly ShellCoordinator _shell;
    private bool _ready;
    public SettingsWindow(ShellCoordinator shell)
    {
        _shell = shell;
        InitializeComponent();
        var saved = shell.Runtime.Settings.Load();
        DarkCheck.IsChecked = saved.Dark;
        PaletteBox.SelectedValue = saved.Palette;
        PositionBox.SelectedValue = saved.BarPosition;
        DockCheck.IsChecked = saved.DockEnabled;
        WidgetsCheck.IsChecked = saved.DesktopWidgetsEnabled;
        _ready = true;
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            MinWidth = Math.Min(680, Math.Max(320, work.Width - 24));
            MinHeight = Math.Min(520, Math.Max(320, work.Height - 24));
            Width = Math.Min(900, work.Width - 24);
            Height = Math.Min(650, work.Height - 24);
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + (work.Height - Height) / 2;
        };
    }
    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var dark = DarkCheck.IsChecked == true;
        var palette = PaletteBox.SelectedValue as string ?? "terracotta";
        _shell.Theme.Apply(dark, palette);
        _shell.Runtime.Settings.Update(saved => saved with { Dark = dark, Palette = palette });
    }
    private void Position_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var edge = PositionBox.SelectedValue as string ?? "top";
        _shell.Runtime.Settings.Update(saved => saved with { BarPosition = edge });
        _shell.Bar.ApplyPosition(edge);
        _shell.Dock.Refresh();
        _shell.PositionDesktopWidgets();
    }
    private void Dock_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var enabled = DockCheck.IsChecked == true;
        _shell.Runtime.Settings.Update(saved => saved with { DockEnabled = enabled });
        if (enabled) _shell.Dock.Show(); else _shell.Dock.Hide();
    }
    private void Control_Click(object sender, RoutedEventArgs e) => _shell.Router.Open(ShellPanel.ControlCenter);
    private void Search_Click(object sender, RoutedEventArgs e) => _shell.Router.Open(ShellPanel.Launcher);
    private void Wallpaper_Click(object sender, RoutedEventArgs e) => _shell.ShowWallpaper();
    private void Widgets_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _shell.Runtime.Settings.Update(saved => saved with { DesktopWidgetsEnabled = WidgetsCheck.IsChecked == true });
        _shell.ApplyDesktopWidgets();
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
