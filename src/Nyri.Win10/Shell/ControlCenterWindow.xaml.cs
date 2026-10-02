using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

public partial class ControlCenterWindow : Window
{
    private readonly ShellCoordinator _shell;
    private bool _refreshingAudio;
    public ControlCenterWindow(ShellCoordinator shell)
    {
        _shell = shell;
        InitializeComponent();
        GreetingText.Text = "Привет, " + Environment.UserName;
        ActivityList.ItemsSource = shell.Runtime.Hub.Activities;
        shell.Runtime.Hub.Changed += Hub_Changed;
        if (shell.Runtime.Audio is { } audio) audio.Changed += Audio_Changed;
        Closed += (_, _) =>
        {
            shell.Runtime.Hub.Changed -= Hub_Changed;
            if (shell.Runtime.Audio is { } audio) audio.Changed -= Audio_Changed;
        };
    }
    private void Position()
    {
        var work = SystemParameters.WorkArea;
        Width = Math.Min(436, work.Width - 24);
        Height = Math.Min(660, work.Height - 24);
        Left = work.Right - Width - 8;
        Top = work.Top + 8;
    }
    public void Reveal() { Position(); Show(); Activate(); Refresh(); }
    private void Window_Loaded(object sender, RoutedEventArgs e) { Position(); Refresh(); }
    private void Hub_Changed(object? sender, EventArgs e) => Refresh();
    private void Audio_Changed(object? sender, EventArgs e) => RefreshAudio();
    private void Refresh()
    {
        var hub = _shell.Runtime.Hub;
        NetworkText.Text = hub.Activities.Any(x => x.Id == "provider:network") ? "Состояние сети недоступно" : hub.Activities.Any(x => x.Id == "network") ? "Нет подключения к сети" : "Сеть Windows доступна";
        MediaControls.Visibility = hub.Activities.Any(x => x.Id == "media") ? Visibility.Visible : Visibility.Collapsed;
        RefreshAudio();
    }
    private void RefreshAudio()
    {
        if (VolumeSlider is null) return;
        _refreshingAudio = true;
        try
        {
            var audio = _shell.Runtime.Audio;
            VolumeSlider.IsEnabled = OutputMuteButton.IsEnabled = audio?.OutputAvailable == true;
            MicrophoneMuteButton.IsEnabled = audio?.MicrophoneAvailable == true;
            VolumeSlider.Value = (audio?.Volume ?? 0) * 100;
            VolumeText.Text = audio?.OutputAvailable == true ? $"{Math.Round(audio.Volume * 100)}%" : "Звук недоступен";
            OutputNameText.Text = audio?.OutputName ?? "Устройство вывода не найдено";
            OutputMuteButton.Content = audio?.OutputMuted == true ? "Включить звук" : "Выключить звук";
            MicrophoneMuteButton.Content = audio?.MicrophoneAvailable != true ? "Микрофон не найден" : audio.MicrophoneMuted ? "Включить микрофон" : "Выключить микрофон";
        }
        finally { _refreshingAudio = false; }
    }
    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_refreshingAudio && IsLoaded) _shell.Runtime.Audio?.SetVolume(e.NewValue / 100);
    }
    private void OutputMute_Click(object sender, RoutedEventArgs e)
    {
        if (_shell.Runtime.Audio is { OutputAvailable: true } audio) audio.SetOutputMuted(!audio.OutputMuted);
    }
    private void MicrophoneMute_Click(object sender, RoutedEventArgs e)
    {
        if (_shell.Runtime.Audio is { MicrophoneAvailable: true } audio) audio.SetMicrophoneMuted(!audio.MicrophoneMuted);
    }
    private void SystemSettings_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string uri) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); _shell.Router.Close(); }
        catch { ErrorText.Text = "Не удалось открыть настройки Windows"; }
    }
    private async void Previous_Click(object sender, RoutedEventArgs e) => await _shell.Runtime.Media.PreviousAsync();
    private async void Play_Click(object sender, RoutedEventArgs e) => await _shell.Runtime.Media.TogglePlayPauseAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _shell.Runtime.Media.NextAsync();
    private void Timer_Click(object sender, RoutedEventArgs e) => _shell.Runtime.Timers.StartCountdown(TimeSpan.FromMinutes(5));
    private void Stopwatch_Click(object sender, RoutedEventArgs e) => _shell.Runtime.Timers.ToggleStopwatch();
    private void Stop_Click(object sender, RoutedEventArgs e) => _shell.Runtime.Timers.Stop();
    private void Settings_Click(object sender, RoutedEventArgs e) => _shell.ShowSettings();
    private void Close_Click(object sender, RoutedEventArgs e) => _shell.Router.Close();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { _shell.Router.Close(); e.Handled = true; } }
    private void Window_Deactivated(object? sender, EventArgs e) { if (IsVisible && !_shell.ChromeHasFocus() && _shell.Router.Current.Panel == ShellPanel.ControlCenter) _shell.Router.Close(); }
}
