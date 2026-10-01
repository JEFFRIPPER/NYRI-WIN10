using System.Windows;
using Nyri.Win10.Models;
using Windows.Media.Control;

namespace Nyri.Win10.Services;

public sealed class MediaSessionService : IDisposable
{
    private readonly ActivityHub _hub;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _disposed;

    public MediaSessionService(ActivityHub hub)
    {
        _hub = hub;
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            await AttachCurrentSessionAsync();
        }
        catch
        {
            RemoveMedia();
        }
    }
    private async void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        await AttachCurrentSessionAsync();
    }

    private async void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        await AttachCurrentSessionAsync();
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
        await PublishAsync();
    }

    private async void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        await PublishAsync();
    }

    private async Task PublishAsync()
    {
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
            var title = string.IsNullOrWhiteSpace(props.Title)
                ? session.SourceAppUserModelId
                : props.Title;
            var detail = string.IsNullOrWhiteSpace(props.Artist)
                ? "Сейчас воспроизводится"
                : props.Artist;

            OnUi(() => _hub.Upsert(new IslandActivity(
                "media",
                IslandActivityKind.Media,
                title,
                detail,
                "♪",
                DateTimeOffset.Now,
                true,
                50)));
        }
        catch
        {
            RemoveMedia();
        }
    }

    private void RemoveMedia()
    {
        OnUi(() => _hub.Remove("media"));
    }

    private static void OnUi(Action action)
    {        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
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
        _disposed = true;
        DetachSession();

        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
        }
    }
}