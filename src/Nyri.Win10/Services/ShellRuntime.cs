using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

/// <summary>Owns the live services shared by all shell surfaces.</summary>
public sealed class ShellRuntime : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly bool _startSystemProviders;
    private readonly object _lifecycle = new();
    private Task? _startTask;
    private SystemStatusService? _systemStatus;
    private PrivacyService? _privacy;
    private ClipboardListener? _clipboard;
    private CancellationTokenSource? _clipboardExpiry;
    private volatile bool _disposed;

    public ActivityHub Hub { get; }
    public SettingsService Settings { get; }
    public TimerService Timers { get; }
    public MediaSessionService Media { get; }
    public AudioService? Audio { get; private set; }
    public SystemResourceService? Resources { get; private set; }
    public bool IsStarted => _startTask?.IsCompletedSuccessfully == true && !_disposed;
    public bool IsDisposed => _disposed;

    // Disabling providers permits lifecycle tests without touching Windows capture,
    // media or network state. Production always uses the default constructor.
    public ShellRuntime(bool startSystemProviders = true)
    {
        _startSystemProviders = startSystemProviders;
        Hub = new ActivityHub();
        Settings = new SettingsService();
        Timers = new TimerService(Hub);
        Media = new MediaSessionService(Hub);
    }

    /// <summary>Returns the same startup task on every call.</summary>
    public Task StartAsync()
    {
        lock (_lifecycle)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ShellRuntime));
            return _startTask ??= _dispatcher.CheckAccess()
                ? StartCoreAsync()
                : _dispatcher.InvokeAsync(StartCoreAsync).Task.Unwrap();
        }
    }

    private async Task StartCoreAsync()
    {
        if (_disposed || !_startSystemProviders) return;
        StartOptional("audio", "Звук", () => Audio = new AudioService(_dispatcher));
        StartOptional("resources", "Показатели системы", () => Resources = new SystemResourceService(_dispatcher));
        StartOptional("network", "Сеть и VPN", () => _systemStatus = new SystemStatusService(Hub));
        StartOptional("privacy", "Микрофон и камера", () => _privacy = new PrivacyService(Hub));
        if (_disposed) return;
        try
        {
            await Media.StartAsync();
            if (!_disposed) AppDiagnostics.Write("Shell runtime started");
        }
        catch (Exception ex)
        {
            PublishUnavailable("media", "Медиа", ex);
        }
    }

    private void StartOptional(string id, string title, Action start)
    {
        if (_disposed) return;
        try { start(); }
        catch (Exception ex) { PublishUnavailable(id, title, ex); }
    }

    private void PublishUnavailable(string id, string title, Exception exception)
    {
        AppDiagnostics.Write($"Provider {id} unavailable: {exception.GetType().Name}: {exception.Message}");
        if (_disposed) return;
        Hub.Upsert(new IslandActivity("provider:" + id, IslandActivityKind.System,
            title + ": недоступно", "Windows не предоставила доступ к сервису", "!",
            DateTimeOffset.Now, Priority: 85));
    }

    /// <summary>Attaches once to the shell's long-lived, initialized window.</summary>
    public void AttachClipboard(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => AttachClipboard(window));
            return;
        }
        if (_disposed) throw new ObjectDisposedException(nameof(ShellRuntime));
        if (_clipboard is not null) return;
        if (new WindowInteropHelper(window).Handle == IntPtr.Zero)
            throw new InvalidOperationException("Attach clipboard after the window source is initialized.");
        StartOptional("clipboard", "Буфер обмена", () => _clipboard = new ClipboardListener(window, PublishClipboard));
    }

    public void PublishClipboard(string text)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => PublishClipboard(text));
            return;
        }
        _clipboardExpiry?.Cancel();
        _clipboardExpiry?.Dispose();
        _clipboardExpiry = new CancellationTokenSource();
        var expiry = _clipboardExpiry;
        Hub.Upsert(new IslandActivity("clipboard", IslandActivityKind.Clipboard,
            "Скопировано", text, "▣", DateTimeOffset.Now, Priority: 65));
        _ = ExpireClipboardAsync(expiry);
    }

    private async Task ExpireClipboardAsync(CancellationTokenSource expiry)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(3500), expiry.Token);
            if (!_disposed && ReferenceEquals(expiry, _clipboardExpiry)) Hub.Remove("clipboard");
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (!_dispatcher.CheckAccess() && !_dispatcher.HasShutdownStarted)
        {
            _dispatcher.Invoke(Dispose);
            return;
        }
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Hub.Seal();
        _clipboardExpiry?.Cancel();
        _clipboardExpiry?.Dispose();
        _clipboard?.Dispose();
        Audio?.Dispose();
        Resources?.Dispose();
        Media.Dispose();
        _systemStatus?.Dispose();
        _privacy?.Dispose();
        Timers.Dispose();
        AppDiagnostics.Write("Shell runtime stopped");
    }
}
