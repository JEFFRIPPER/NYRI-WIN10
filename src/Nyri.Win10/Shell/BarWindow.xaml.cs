using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

public partial class BarWindow : Window
{
    private readonly ShellCoordinator _shell;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private AppBarService? _appBar;
    private GlobalHotkeyService? _hotkeys;

    public BarWindow(ShellCoordinator shell)
    {
        _shell = shell;
        InitializeComponent();
        _shell.Runtime.Hub.Changed += Hub_Changed;
        _shell.Windows.Changed += Windows_Changed;
        _clock.Tick += (_, _) => Refresh();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _shell.Runtime.AttachClipboard(this);
        _appBar = new AppBarService(this, 64, _shell.Runtime.Settings.Load().BarPosition);
        _hotkeys = new GlobalHotkeyService(this);
        _hotkeys.Register(1, 0x20, () => _shell.Router.Toggle(ShellPanel.Launcher));
        _hotkeys.Register(2, 0x4E, () => _shell.Router.Toggle(ShellPanel.ControlCenter));
        _hotkeys.Register(3, 0x49, _shell.ShowSettings);
        _hotkeys.Register(4, 0x51, () => Application.Current.Shutdown());
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _clock.Start();
        ApplyResponsiveLayout();
        Refresh();
        AppDiagnostics.Write($"Bar visible={IsVisible}; bounds={Left},{Top},{Width},{Height}");
    }

    public void ApplyPosition(string edge) => _appBar?.SetEdge(edge);
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();
    private void ApplyResponsiveLayout()
    {
        // Monitor resolution and scaling can leave far less than 1200 logical pixels.
        // Keep launcher, clock, privacy indicators and controls reachable before labels.
        if (LiveButton is null) return;
        var available = ActualWidth > 0 ? ActualWidth : Width;
        WindowTitleText.Visibility = available < 1120 ? Visibility.Collapsed : Visibility.Visible;
        DateText.Visibility = available < 1000 ? Visibility.Collapsed : Visibility.Visible;
        BrandBadge.Visibility = available < 620 ? Visibility.Collapsed : Visibility.Visible;
        ActivityTitle.Visibility = available < 620 ? Visibility.Collapsed : Visibility.Visible;
        ActivityTitle.MaxWidth = available < 760 ? 72 : available < 1120 ? 120 : 180;
        LiveButton.MinWidth = available < 480 ? 130 : available < 620 ? 180 : available < 760 ? 220 : 260;
    }
    private void Hub_Changed(object? sender, EventArgs e) => Refresh();
    private void Windows_Changed(object? sender, EventArgs e) => WindowTitleText.Text = _shell.Windows.CurrentTitle;

    private void Refresh()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        DateText.Text = DateTime.Now.ToString("ddd, d MMM", CultureInfo.GetCultureInfo("ru-RU"));
        var primary = _shell.Runtime.Hub.Primary;
        ActivityTitle.Text = primary?.Title ?? "NYRI";
        ActivityGlyph.Text = primary?.Glyph ?? "◆";
        MicrophoneText.Visibility = _shell.Runtime.Hub.Activities.Any(x => x.Id == "privacy:microphone") ? Visibility.Visible : Visibility.Collapsed;
        CameraText.Visibility = _shell.Runtime.Hub.Activities.Any(x => x.Id == "privacy:webcam") ? Visibility.Visible : Visibility.Collapsed;
        WindowTitleText.Text = _shell.Windows.CurrentTitle;
    }

    private void Launcher_Click(object sender, RoutedEventArgs e) => _shell.Router.Toggle(ShellPanel.Launcher);
    private void Control_Click(object sender, RoutedEventArgs e) => _shell.Router.Toggle(ShellPanel.ControlCenter);
    private void Live_Click(object sender, RoutedEventArgs e) => _shell.Router.Toggle(ShellPanel.Live);
    private void Settings_Click(object sender, RoutedEventArgs e) => _shell.ShowSettings();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _clock.Stop();
        _shell.Runtime.Hub.Changed -= Hub_Changed;
        _shell.Windows.Changed -= Windows_Changed;
        _hotkeys?.Dispose();
        _appBar?.Dispose();
    }
}
