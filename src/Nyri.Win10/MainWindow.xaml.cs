using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nyri.Win10.Models;
using Nyri.Win10.Services;

namespace Nyri.Win10;

public partial class MainWindow : Window
{
    private const double CompactWidth = 260;
    private const double CompactHeight = 70;
    private const double ExpandedWidth = 430;
    private const double ExpandedHeight = 300;

    private readonly ActivityHub _hub = new();
    private readonly SettingsService _settings = new();
    private readonly TimerService _timerService;
    private readonly DispatcherTimer _clockTimer;
    private PrivacyService? _privacy;
    private SystemStatusService? _systemStatus;
    private MediaSessionService? _mediaSession;
    private ClipboardListener? _clipboardListener;
    private CancellationTokenSource? _clipboardCts;
    private bool _expanded;
    private bool _manualPosition;

    public MainWindow()
    {
        InitializeComponent();
        _timerService = new TimerService(_hub);
        ActivityList.ItemsSource = _hub.Activities;
        _hub.Changed += (_, _) => UpdateHeader();
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateClock();

        _hub.Upsert(new IslandActivity(
            "ready",
            IslandActivityKind.System,
            "NYRI готов",
            "Windows live island",
            "◆",
            DateTimeOffset.Now,
            true,
            1));
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _clipboardListener = new ClipboardListener(this, OnClipboard);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Place and paint the island before waiting for optional Windows providers.
        RestorePosition();
        _clockTimer.Start();
        UpdateClock();
        UpdateHeader();
        AppDiagnostics.Write($"Window loaded: visible={IsVisible}, left={Left}, top={Top}, size={ActualWidth}x{ActualHeight}");
        (Application.Current as App)?.WindowReady(this);
        _privacy = new PrivacyService(_hub);
        _systemStatus = new SystemStatusService(_hub);
        _mediaSession = new MediaSessionService(_hub);
        await _mediaSession.StartAsync();

        AppDiagnostics.Write("Media provider initialized");
    }
    public void Reveal()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Show();
        WindowState = WindowState.Normal;
        // A second launch is an explicit recovery request; bring the island into view.
        _manualPosition = false;
        _settings.Save(new AppSettings());
        CenterAtTop();
        Activate();
        Focus();
        AppDiagnostics.Write($"Window revealed: visible={IsVisible}, left={Left}, top={Top}");
    }
    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
    }

    private void UpdateHeader()
    {
        Dispatcher.Invoke(() =>
        {
            MediaControls.Visibility = _hub.Activities.Any(x => x.Id == "media")
                ? Visibility.Visible
                : Visibility.Collapsed;

            MicrophoneIndicator.Visibility = _hub.Activities.Any(x => x.Id == "privacy:microphone") ? Visibility.Visible : Visibility.Collapsed;
            CameraIndicator.Visibility = _hub.Activities.Any(x => x.Id == "privacy:webcam") ? Visibility.Visible : Visibility.Collapsed;
            var primary = _hub.Primary;
            if (primary is null)
            {
                StatusGlyph.Text = "◆";
                TitleText.Text = "NYRI";
                DetailText.Text = "Windows live island";
                return;
            }

            StatusGlyph.FontFamily = new FontFamily(primary.Kind is IslandActivityKind.Microphone or IslandActivityKind.Camera ? "Segoe MDL2 Assets" : "Segoe UI");
            StatusGlyph.Text = primary.Glyph;
            TitleText.Text = primary.Title;
            DetailText.Text = primary.Detail;
        });
    }

    private async void OnClipboard(string text)
    {
        _clipboardCts?.Cancel();
        _clipboardCts = new CancellationTokenSource();
        var token = _clipboardCts.Token;
        _hub.Upsert(new IslandActivity(
            "clipboard",
            IslandActivityKind.Clipboard,
            "Скопировано",
            text,
            "▣",
            DateTimeOffset.Now,
            true,
            65));

        try
        {
            await Task.Delay(3500, token);
            _hub.Remove("clipboard");
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindParent<System.Windows.Controls.Button>(source) is not null)
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _manualPosition = true;
            DragMove();
            return;
        }

        ToggleExpanded();
    }

    private static T? FindParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private async void MediaPrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaSession is not null)
            await _mediaSession.PreviousAsync();
    }

    private async void MediaPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaSession is not null)
            await _mediaSession.TogglePlayPauseAsync();
    }

    private async void MediaNext_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaSession is not null)
            await _mediaSession.NextAsync();
    }

    private void StartFiveMinuteTimer_Click(object sender, RoutedEventArgs e)
        => _timerService.StartCountdown(TimeSpan.FromMinutes(5));

    private void ToggleStopwatch_Click(object sender, RoutedEventArgs e)
        => _timerService.ToggleStopwatch();

    private void StopTimer_Click(object sender, RoutedEventArgs e)
        => _timerService.Stop();

    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        var duration = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 240 : 0);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (_expanded)
        {
            ExpandedPanel.Visibility = Visibility.Visible;
            ExpandedPanel.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            AnimateWindow(ExpandedWidth, ExpandedHeight, duration, ease);
        }
        else
        {
            var fade = new DoubleAnimation(ExpandedPanel.Opacity, 0, duration)
            {
                EasingFunction = ease
            };
            fade.Completed += (_, _) => { if (!_expanded) ExpandedPanel.Visibility = Visibility.Collapsed; };
            ExpandedPanel.BeginAnimation(OpacityProperty, fade);
            AnimateWindow(CompactWidth, CompactHeight, duration, ease);
        }
    }

    private void AnimateWindow(double width, double height, TimeSpan duration, IEasingFunction ease)
    {
        BeginAnimation(
            WidthProperty,
            new DoubleAnimation(ActualWidth, width, duration) { EasingFunction = ease });
        BeginAnimation(
            HeightProperty,
            new DoubleAnimation(ActualHeight, height, duration) { EasingFunction = ease });
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Q && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Close();
            return;
        }

        if (e.Key == Key.R && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _manualPosition = false;
            _settings.Save(new AppSettings());
            CenterAtTop();
            return;
        }

        if (e.Key == Key.Escape && _expanded)
            ToggleExpanded();
    }

    private void RestorePosition()
    {
        var saved = _settings.Load();
        if (saved.Left is double left &&
            saved.Top is double top &&
            IsOnVirtualScreen(left, top))
        {
            Left = left;
            Top = top;
            _manualPosition = true;
            return;
        }

        CenterAtTop();
    }

    private static bool IsOnVirtualScreen(double left, double top)
    {
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        return left + 100 > virtualLeft &&
               left < virtualRight - 40 &&
               top + 40 > virtualTop &&
               top < virtualBottom - 30;
    }

    private void CenterAtTop()
    {
        if (_manualPosition) return;

        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - ActualWidth) / 2;
        Top = work.Top + 8;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        CenterAtTop();
    }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_manualPosition)
            _settings.Save(new AppSettings(Left, Top));

        _clockTimer.Stop();
        _clipboardCts?.Cancel();
        _clipboardCts?.Dispose();
        _clipboardListener?.Dispose();
        _mediaSession?.Dispose();
        _systemStatus?.Dispose();
        _privacy?.Dispose();
        _timerService.Dispose();
    }
}
