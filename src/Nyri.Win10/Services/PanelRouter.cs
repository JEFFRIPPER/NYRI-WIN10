using System.Windows;
using System.Windows.Threading;

namespace Nyri.Win10.Services;

public enum ShellPanel
{
    None,
    Launcher,
    ControlCenter,
    Clipboard,
    Live
}

public sealed record PanelRoute(ShellPanel Panel, string? Page = null, Rect? Anchor = null)
{
    public static PanelRoute Empty { get; } = new(ShellPanel.None);
}

/// <summary>Routes one transient shell panel without owning any windows.</summary>
public sealed class PanelRouter : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _disposed;

    public PanelRoute Current { get; private set; } = PanelRoute.Empty;
    public event EventHandler? Changed;

    public void Open(ShellPanel panel, string? page = null, Rect? anchor = null)
        => OnDispatcher(() => SetRoute(panel == ShellPanel.None
            ? PanelRoute.Empty : new PanelRoute(panel, Normalize(page), anchor)));

    public void Toggle(ShellPanel panel, string? page = null, Rect? anchor = null)
        => OnDispatcher(() =>
        {
            var normalizedPage = Normalize(page);
            SetRoute(panel == ShellPanel.None ||
                (Current.Panel == panel && string.Equals(Current.Page, normalizedPage, StringComparison.Ordinal))
                ? PanelRoute.Empty : new PanelRoute(panel, normalizedPage, anchor));
        });

    public void Close() => OnDispatcher(() => SetRoute(PanelRoute.Empty));

    private void SetRoute(PanelRoute route)
    {
        if (_disposed || Current == route) return;
        Current = route;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnDispatcher(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }

    private static string? Normalize(string? page)
        => string.IsNullOrWhiteSpace(page) ? null : page.Trim();

    public void Dispose()
    {
        if (!_dispatcher.CheckAccess() && !_dispatcher.HasShutdownStarted)
        {
            _dispatcher.Invoke(Dispose);
            return;
        }
        _disposed = true;
        Changed = null;
    }
}
