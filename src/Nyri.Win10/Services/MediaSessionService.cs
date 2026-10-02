using System.Windows.Threading;
using Nyri.Win10.Models;
using Windows.Media.Control;

namespace Nyri.Win10.Services;

public sealed class MediaSessionService : IDisposable
{
    private readonly ActivityHub _hub;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private volatile bool _disposed;
    private Task? _startTask;
    public bool IsAvailable { get; private set; }

    public MediaSessionService(ActivityHub hub)
    {
        _hub = hub;
    }

    public Task StartAsync()
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return Task.CompletedTask;
        if (!_dispatcher.CheckAccess()) return _dispatcher.InvokeAsync(StartAsync).Task.Unwrap();
        return _startTask ??= StartCoreAsync();
    }

    private async Task StartCoreAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed) return;
            _manager = manager;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            IsAvailable = true;
            _hub.Remove("provider:media");
            await AttachCurrentSessionAsync();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            AppDiagnostics.Write($"Media provider unavailable: {ex.GetType().Name}: {ex.Message}");
            OnUi(() => _hub.Upsert(new IslandActivity("provider:media", IslandActivityKind.System,
                "Медиа: недоступно", "Windows не предоставила доступ к медиасессиям", "!",
                DateTimeOffset.Now, Priority: 85)));
            RemoveMedia();
        }
    }
    private async void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        await OnUiAsync(AttachCurrentSessionAsync);
    }

    private async void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        await OnUiAsync(AttachCurrentSessionAsync);
    }

    private async Task AttachCurrentSessionAsync()
    {
        if (_disposed || _manager is null)
            return;

        DetachSession();
        _session = _manager.GetCurrentSession();

        if (_session is null)
        {
            RemoveMedia();
            return;
        }

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        await PublishAsync();
    }
    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        if (!_disposed && ReferenceEquals(sender, _session)) await OnUiAsync(PublishAsync);
    }

    private async void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        if (!_disposed && ReferenceEquals(sender, _session)) await OnUiAsync(PublishAsync);
    }

    private async Task PublishAsync()
    {
        if (_disposed) return;
        var session = _session;
        if (session is null)
        {
            RemoveMedia();
            return;
        }

        try
        {
            var playback = session.GetPlaybackInfo();
            if (playback?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                RemoveMedia();
                return;
            }
            var props = await session.TryGetMediaPropertiesAsync();
            if (_disposed || !ReferenceEquals(session, _session)) return;
            var title = string.IsNullOrWhiteSpace(props.Title)
                ? session.SourceAppUserModelId
                : props.Title;
            var detail = string.IsNullOrWhiteSpace(props.Artist)
                ? "Сейчас воспроизводится"
                : props.Artist;

            OnUi(() =>
            {
                if (!ReferenceEquals(session, _session)) return;
                _hub.Upsert(new IslandActivity(
                    "media",
                    IslandActivityKind.Media,
                    title,
                    detail,
                    "♪",
                    DateTimeOffset.Now,
                    true,
                    50));
            });
        }
        catch
        {
            if (ReferenceEquals(session, _session)) RemoveMedia();
        }
    }

    private void RemoveMedia()
    {
        OnUi(() => _hub.Remove("media"));
    }

    private void OnUi(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }

    private async Task OnUiAsync(Func<Task> action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        try
        {
            if (_dispatcher.CheckAccess()) await action();
            else await _dispatcher.InvokeAsync(() => _disposed ? Task.CompletedTask : action()).Task.Unwrap();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write($"Media session refresh failed: {ex.GetType().Name}: {ex.Message}");
            RemoveMedia();
        }
    }

    private void DetachSession()
    {
        if (_session is null)
            return;

        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session = null;
    }

    public async Task PreviousAsync()
    {
        try
        {
            if (_session is not null)
                await _session.TrySkipPreviousAsync();
        }
        catch { }
    }

    public async Task TogglePlayPauseAsync()
    {
        try
        {
            if (_session is not null)
                await _session.TryTogglePlayPauseAsync();
        }
        catch { }
    }

    public async Task NextAsync()
    {
        try
        {
            if (_session is not null)
                await _session.TrySkipNextAsync();
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsAvailable = false;
        DetachSession();

        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager = null;
        }
    }
}
