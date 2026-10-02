using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

/// <summary>Desktop clock/media or calendar/resource widgets sharing the shell runtime.</summary>
public partial class DesktopWidgetWindow : Window
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private readonly ShellCoordinator _shell;
    private readonly SystemResourceService _resources;
    private readonly bool _rightSide;
    private readonly DispatcherTimer _clock;
    private DateTime _calendarDate;
    private bool _subscribed;
    private bool _closed;
    private bool _dragging;

    public DesktopWidgetWindow(ShellCoordinator shell, SystemResourceService resources, bool rightSide)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _rightSide = rightSide;
        InitializeComponent();
        Width = rightSide ? 270 : 220;
        LeftWidgets.Visibility = rightSide ? Visibility.Collapsed : Visibility.Visible;
        RightWidgets.Visibility = rightSide ? Visibility.Visible : Visibility.Collapsed;
        _clock = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clock.Tick += Clock_Tick;
        IsVisibleChanged += Window_VisibilityChanged;
        Closed += Window_Closed;
        BuildClock();
        UpdateClock();
        UpdateActivities();
        UpdateResources();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        // Keep widgets interactive without bringing a desktop window in front of
        // the user's active application or adding an Alt+Tab/taskbar entry.
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr(style | 0x80 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _shell.Runtime.Hub.Changed += Activity_Changed;
            _resources.Changed += Resources_Changed;
            _subscribed = true;
        }
        Position();
        UpdateClock();
        UpdateActivities();
        UpdateResources();
        _clock.Start();
    }

    public void Position()
    {
        if (_closed || _dragging) return;
        var work = SystemParameters.WorkArea;
        MaxHeight = Math.Max(120, work.Height - 48);
        var settings = _shell.Runtime.Settings.Load();
        var savedX = _rightSide ? settings.WidgetRightX : settings.WidgetLeftX;
        var savedY = _rightSide ? settings.WidgetRightY : settings.WidgetLeftY;
        var hasPosition = savedX is double x && double.IsFinite(x) &&
            savedY is double y && double.IsFinite(y) && work.Contains(new Point(x, y));
        var left = hasPosition ? savedX!.Value : _rightSide ? work.Right - Width - 16 : work.Left + 16;
        var top = hasPosition ? savedY!.Value : work.Top + 24;
        (Left, Top) = ClampPosition(work, left, top);
    }

    private (double Left, double Top) ClampPosition(Rect work, double left, double top)
        => (Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - Width)),
            Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - Math.Max(0, ActualHeight))));

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => Position();

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_closed || e.ChangedButton != MouseButton.Left || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        _dragging = true;
        try
        {
            DragMove();
            if (_closed) return;
            (Left, Top) = ClampPosition(SystemParameters.WorkArea, Left, Top);
            var left = Left;
            var top = Top;
            _shell.Runtime.Settings.Update(settings => _rightSide
                ? settings with { WidgetRightX = left, WidgetRightY = top }
                : settings with { WidgetLeftX = left, WidgetLeftY = top });
        }
        finally { _dragging = false; }
    }

    private void Window_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (IsVisible)
        {
            UpdateClock();
            UpdateActivities();
            UpdateResources();
            Position();
            _clock.Start();
        }
        else _clock.Stop();
    }

    private void Clock_Tick(object? sender, EventArgs e) => UpdateClock();

    private void UpdateClock()
    {
        if (_closed) return;
        var now = DateTime.Now;
        if (!_rightSide)
        {
            HourHand.RenderTransform = new RotateTransform((now.Hour % 12) * 30 + now.Minute * 0.5, 106, 106);
            MinuteHand.RenderTransform = new RotateTransform(now.Minute * 6 + now.Second * 0.1, 106, 106);
            DigitalTimeText.Text = now.ToString("HH:mm", Russian);
            DateText.Text = now.ToString("dddd, d MMMM", Russian);
        }
        else if (_calendarDate != now.Date)
        {
            _calendarDate = now.Date;
            BuildCalendar(now.Date);
        }
    }

    private void BuildClock()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < 240; i++)
            {
                var angle = i * Math.PI * 2 / 240;
                var radius = 94 + 6 * Math.Cos(angle * 12);
                var point = new Point(106 + radius * Math.Sin(angle), 106 - radius * Math.Cos(angle));
                if (i == 0) context.BeginFigure(point, true, true);
                else context.LineTo(point, true, false);
            }
        }
        geometry.Freeze();
        ClockFlower.Data = geometry;
        for (var i = 0; i < 12; i++)
        {
            var angle = i * Math.PI / 6;
            var dot = new Ellipse { Width = 3, Height = 3, Opacity = 0.7 };
            dot.SetResourceReference(Shape.FillProperty, "OnPrimaryContainerBrush");
            Canvas.SetLeft(dot, 104.5 + Math.Sin(angle) * 76);
            Canvas.SetTop(dot, 104.5 - Math.Cos(angle) * 76);
            ClockMarks.Children.Add(dot);
        }
    }

    private void BuildCalendar(DateTime today)
    {
        CalendarTitleText.Text = Russian.TextInfo.ToTitleCase(today.ToString("MMMM yyyy", Russian));
        CalendarDays.Children.Clear();
        var first = new DateTime(today.Year, today.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        for (var i = 0; i < offset; i++) CalendarDays.Children.Add(new Border());
        for (var day = 1; day <= DateTime.DaysInMonth(today.Year, today.Month); day++)
        {
            var current = day == today.Day;
            var text = new TextBlock
            {
                Text = day.ToString(Russian), FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, current ? "OnPrimaryBrush" : "OnSurfaceBrush");
            var cell = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = text,
                ToolTip = new DateTime(today.Year, today.Month, day).ToString("D", Russian)
            };
            if (current) cell.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");
            CalendarDays.Children.Add(cell);
        }
    }

    private void Activity_Changed(object? sender, EventArgs e) { if (IsVisible) UpdateActivities(); }
    private void Resources_Changed(object? sender, EventArgs e) { if (IsVisible) UpdateResources(); }

    private void UpdateActivities()
    {
        if (_closed || _rightSide) return;
        var media = _shell.Runtime.Hub.Activities.FirstOrDefault(x => x.Id == "media" && x.IsActive);
        MediaTitleText.Text = media?.Title ?? "Нет воспроизведения";
        MediaDetailText.Text = media?.Detail ?? "Трек появится здесь автоматически";
        MediaControls.Visibility = media is null ? Visibility.Collapsed : Visibility.Visible;
        var timer = _shell.Runtime.Hub.Activities.FirstOrDefault(x => x.Id == "timer" && x.IsActive);
        TimerCard.Visibility = timer is null ? Visibility.Collapsed : Visibility.Visible;
        TimerTitleText.Text = timer?.Title ?? "";
        TimerValueText.Text = timer?.Detail ?? "";
    }

    private void UpdateResources()
    {
        if (_closed || !_rightSide) return;
        SetResourceValue(CpuText, CpuBar, _resources.CpuPercent);
        SetResourceValue(MemoryText, MemoryBar, _resources.MemoryPercent);
        BatteryText.Text = _resources.BatteryAvailable
            ? "Батарея · " + FormatPercent(_resources.BatteryPercent)
            : "Батарея недоступна";
        PowerText.Text = _resources.OnAC switch
        {
            true => "Питание от сети",
            false => "Питание от батареи",
            _ => "Источник питания неизвестен"
        };
    }

    private static void SetResourceValue(TextBlock label, ProgressBar bar, double? value)
    {
        label.Text = FormatPercent(value);
        bar.Value = value ?? 0;
        bar.Opacity = value is null ? 0.3 : 1;
        bar.ToolTip = value is null ? "Windows не предоставила показатель" : label.Text;
    }

    private static string FormatPercent(double? value) => value is double number ? $"{number:0}%" : "—";

    private async void Previous_Click(object sender, RoutedEventArgs e) => await _shell.Runtime.Media.PreviousAsync();
    private async void Play_Click(object sender, RoutedEventArgs e) => await _shell.Runtime.Media.TogglePlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _shell.Runtime.Media.NextAsync();
    private void StopTimer_Click(object sender, RoutedEventArgs e) => _shell.Runtime.Timers.Stop();
    private void Control_Click(object sender, RoutedEventArgs e) => _shell.Router.Toggle(ShellPanel.ControlCenter);
    private void Settings_Click(object sender, RoutedEventArgs e) => _shell.ShowSettings();

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (_closed) return;
        _closed = true;
        _clock.Stop();
        _clock.Tick -= Clock_Tick;
        IsVisibleChanged -= Window_VisibilityChanged;
        if (_subscribed)
        {
            _shell.Runtime.Hub.Changed -= Activity_Changed;
            _resources.Changed -= Resources_Changed;
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
