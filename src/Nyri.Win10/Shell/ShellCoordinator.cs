using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Nyri.Win10.Models;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

/// <summary>Coordinates independent shell windows over one shared provider runtime.</summary>
public sealed class ShellCoordinator : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private LauncherWindow? _launcher;
    private ControlCenterWindow? _control;
    private SettingsWindow? _settings;
    private WallpaperWindow? _wallpaper;
    private DesktopWidgetWindow? _leftWidgets;
    private DesktopWidgetWindow? _rightWidgets;
    private MainWindow? _island;
    private bool _changingRoute;
    private bool _disposed;

    public ShellRuntime Runtime { get; }
    public ThemeService Theme { get; }
    public PanelRouter Router { get; } = new();
    public ApplicationCatalogService Catalog { get; } = new();
    public WindowTrackerService Windows { get; }
    public IReadOnlyList<LaunchableApp> Apps { get; private set; } = Array.Empty<LaunchableApp>();
    public IReadOnlyList<LaunchableApp> PinnedApps { get; private set; } = Array.Empty<LaunchableApp>();
    public bool CatalogLoading { get; private set; } = true;
    public BarWindow Bar { get; }
    public DockWindow Dock { get; }

    public ShellCoordinator(ShellRuntime runtime, ThemeService theme)
    {
        Runtime = runtime;
        Theme = theme;
        Windows = new WindowTrackerService(Application.Current.Dispatcher);
        Windows.Changed += Windows_Changed;
        Bar = new BarWindow(this);
        Dock = new DockWindow(this);
        Router.Changed += Route_Changed;
    }

    public async Task StartAsync()
    {
        Bar.Show();
        if (Runtime.Settings.Load().DockEnabled) Dock.Show();
        var providers = Runtime.StartAsync();
        ApplyDesktopWidgets();
        try
        {
            Apps = await Catalog.LoadAsync(_lifetime.Token);
            if (_disposed) return;
            CatalogLoading = false;
            RefreshPins();
            _launcher?.Refresh();
            AppDiagnostics.Write($"Shell catalog loaded: {Apps.Count} applications");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            CatalogLoading = false;
            ReportError("Индекс приложений недоступен");
            AppDiagnostics.Write("Catalog error: " + ex.GetType().Name);
        }
        await providers;
    }

    public void Reveal()
    {
        if (_disposed) return;
        Bar.Show();
        Bar.ApplyPosition(Runtime.Settings.Load().BarPosition);
        if (Runtime.Settings.Load().DockEnabled) Dock.Show();
        Router.Open(ShellPanel.Launcher);
        ApplyDesktopWidgets();
    }

    public void ShowSettings()
    {
        Router.Close();
        if (_settings is null)
        {
            _settings = new SettingsWindow(this);
            _settings.Closed += (_, _) => _settings = null;
        }
        _settings.Show();
        _settings.Activate();
    }

    public void ShowWallpaper()
    {
        Router.Close();
        if (_wallpaper is null)
        {
            _wallpaper = new WallpaperWindow(this);
            _wallpaper.Closed += (_, _) => _wallpaper = null;
        }
        _wallpaper.Show();
        _wallpaper.Activate();
    }

    public void ApplyDesktopWidgets()
    {
        if (!Runtime.Settings.Load().DesktopWidgetsEnabled)
        {
            _leftWidgets?.Hide(); _rightWidgets?.Hide();
            return;
        }
        if (Runtime.Resources is not { } resources) return;
        _leftWidgets ??= new DesktopWidgetWindow(this, resources, rightSide: false);
        _rightWidgets ??= new DesktopWidgetWindow(this, resources, rightSide: true);
        _leftWidgets.Show(); _rightWidgets.Show();
        PositionDesktopWidgets();
    }

    public void PositionDesktopWidgets() { _leftWidgets?.Position(); _rightWidgets?.Position(); }

    public void TogglePin(LaunchableApp app)
    {
        var saved = Runtime.Settings.Load();
        var pins = (saved.PinnedApps ?? PinnedApps.Select(x => x.Id).ToArray()).ToList();
        if (!pins.Remove(app.Id)) pins.Add(app.Id);
        Runtime.Settings.Save(saved with { PinnedApps = pins.Distinct().ToArray() });
        RefreshPins();
    }

    private void RefreshPins()
    {
        var ids = Runtime.Settings.Load().PinnedApps;
        PinnedApps = ids is null
            ? Apps.Where(x => x.Id is "windows:explorer" or "windows:settings").ToArray()
            : ids.Select(id => Apps.FirstOrDefault(x => x.Id == id)).OfType<LaunchableApp>().ToArray();
        Dock.Refresh();
    }

    public void ReportError(string message) => Runtime.Hub.Upsert(new IslandActivity(
        "shell:error", IslandActivityKind.System, message, "", "!", DateTimeOffset.Now, Priority: 80));

    public bool ChromeHasFocus()
    {
        var foreground = GetForegroundWindow();
        return foreground != IntPtr.Zero &&
            (foreground == new WindowInteropHelper(Bar).Handle || foreground == new WindowInteropHelper(Dock).Handle);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    private void Windows_Changed(object? sender, EventArgs e)
    {
        if (_launcher?.IsVisible == true) _launcher.Refresh();
    }

    private void Route_Changed(object? sender, EventArgs e)
    {
        if (_disposed || _changingRoute) return;
        _changingRoute = true;
        try
        {
            var route = Router.Current;
            if (route.Panel != ShellPanel.Launcher) _launcher?.Hide();
            if (route.Panel != ShellPanel.ControlCenter) _control?.Hide();
            if (route.Panel != ShellPanel.Live) _island?.Hide();
            if (route.Panel == ShellPanel.Launcher)
            {
                _launcher ??= new LauncherWindow(this);
                _launcher.Reveal();
            }
            else if (route.Panel == ShellPanel.ControlCenter)
            {
                _control ??= new ControlCenterWindow(this);
                _control.Reveal();
            }
            else if (route.Panel == ShellPanel.Live)
            {
                if (_island is null)
                {
                    _island = new MainWindow(Runtime);
                    _island.PanelDismissed += (_, _) => Router.Close();
                    _island.Closed += (_, _) => { _island = null; Router.Close(); };
                }
                _island.Reveal();
                _island.ExpandForPanel();
            }
        }
        finally { _changingRoute = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Router.Changed -= Route_Changed;
        Router.Dispose();
        _launcher?.Close();
        _control?.Close();
        _settings?.Close();
        _wallpaper?.Close();
        _leftWidgets?.Close();
        _rightWidgets?.Close();
        _island?.Close();
        Dock.Close();
        Bar.Close();
        Windows.Changed -= Windows_Changed;
        Windows.Dispose();
        _lifetime.Dispose();
    }
}
