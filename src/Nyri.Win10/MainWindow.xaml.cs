using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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
    private const double ExpandedHeight = 250;

    private readonly ActivityHub _hub = new();
    private readonly DispatcherTimer _clockTimer;
    private SystemStatusService? _systemStatus;
    private ClipboardListener? _clipboardListener;
    private CancellationTokenSource? _clipboardCts;
    private bool _expanded;
    private bool _manualPosition;

    public MainWindow()
    {
        InitializeComponent();
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
            DateTimeOffset.Now));
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _clipboardListener = new ClipboardListener(this, OnClipboard);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _systemStatus = new SystemStatusService(_hub);
        _clockTimer.Start();
        UpdateClock();
        UpdateHeader();
        CenterAtTop();
    }
    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
    }

    private void UpdateHeader()
    {
        Dispatcher.Invoke(() =>
        {
            var primary = _hub.Primary;
            if (primary is null)
            {
                StatusGlyph.Text = "◆";
                TitleText.Text = "NYRI";
                DetailText.Text = "Windows live island";
                return;
            }

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
            DateTimeOffset.Now));

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
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _manualPosition = true;
            DragMove();
            return;
        }

        ToggleExpanded();
    }
    private void ToggleExpanded()
    {
        _expanded = !_expanded;
        var duration = TimeSpan.FromMilliseconds(240);
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
            fade.Completed += (_, _) => ExpandedPanel.Visibility = Visibility.Collapsed;
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

        if (e.Key == Key.Escape && _expanded)
            ToggleExpanded();
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
        _clockTimer.Stop();
        _clipboardCts?.Cancel();
        _clipboardCts?.Dispose();
        _clipboardListener?.Dispose();
        _systemStatus?.Dispose();
    }
}