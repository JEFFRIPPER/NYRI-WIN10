using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nyri.Win10.Models;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

public sealed record SearchResult(string Name, string Detail, string Glyph, ImageSource? Icon, LaunchableApp? App = null, TrackedWindow? Window = null, TimeSpan? Timer = null);

public partial class LauncherWindow : Window
{
    private readonly ShellCoordinator _shell;
    public LauncherWindow(ShellCoordinator shell)
    {
        _shell = shell;
        InitializeComponent();
    }
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Position();
        Refresh();
        SearchBox.Focus();
    }
    public void Reveal()
    {
        SearchBox.Clear();
        Position();
        Show();
        Activate();
        SearchBox.Focus();
    }
    private void Position()
    {
        var work = SystemParameters.WorkArea;
        Width = Math.Min(650, work.Width - 32);
        Height = Math.Min(560, work.Height - 40);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + Math.Min(70, Math.Max(12, (work.Height - Height) / 3));
    }
    private void Search_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) { if (Results is not null) Refresh(); }
    public void Refresh()
    {
        var query = SearchBox.Text.Trim();
        var results = new List<SearchResult>();
        if (TryTimer(query, out var duration)) results.Add(new("Таймер " + duration.ToString(duration.Days > 0 ? @"d\.hh\:mm\:ss" : @"hh\:mm\:ss"), "Enter — запустить", "\uE916", null, Timer: duration));
        results.AddRange(_shell.Apps.Where(x => Matches(x.Name, query)).Take(80)
            .Select(x => new SearchResult(x.Name, "Приложение", x.Glyph, x.Icon, App: x)));
        if (query.Length > 0) results.InsertRange(0, _shell.Windows.Windows.Where(x => Matches(x.Title + " " + x.ProcessName, query)).Take(12)
            .Select(x => new SearchResult(x.Title, "Открытое окно · " + x.ProcessName, "\uE737", x.Icon, Window: x)));
        Results.ItemsSource = results;
        Results.SelectedIndex = results.Count == 0 ? -1 : 0;
        StatusText.Text = _shell.CatalogLoading ? "Читаю приложения Windows…" : results.Count == 0 ? "Ничего не найдено" : "Enter открыть · Ctrl+Enter закрепить · Esc закрыть";
    }
    private static bool Matches(string text, string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    private static bool TryTimer(string text, out TimeSpan duration)
    {
        duration = default;
        var parts = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] is not ("таймер" or "timer")) return false;
        var value = parts[1];
        var unit = value[^1];
        if (!double.TryParse(value[..^1].Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number <= 0) return false;
        var seconds = unit switch { 'м' or 'm' => number * 60, 'ч' or 'h' => number * 3600, 'с' or 's' => number, _ => 0 };
        if (seconds < 1 || seconds > 7 * 24 * 3600) return false;
        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }
    private void OpenSelected()
    {
        if (Results.SelectedItem is not SearchResult result) return;
        if (result.App is not null)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { _shell.TogglePin(result.App); StatusText.Text = "Закрепление в доке изменено"; return; }
            if (!_shell.Catalog.Launch(result.App, out var error)) { StatusText.Text = error; return; }
        }
        else if (result.Window is not null && !_shell.Windows.Activate(result.Window))
        {
            StatusText.Text = "Windows не разрешила переключение. Окно могло быть закрыто.";
            return;
        }
        else if (result.Timer is TimeSpan timer) _shell.Runtime.Timers.StartCountdown(timer);
        _shell.Router.Close();
    }
    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _shell.Router.Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Up)
        {
            if (Results.Items.Count == 0) { e.Handled = true; return; }
            Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, Results.Items.Count - 1));
            if (Results.SelectedItem is not null) Results.ScrollIntoView(Results.SelectedItem);
            e.Handled = true;
        }
    }
    private void Window_Deactivated(object? sender, EventArgs e) { if (IsVisible && !_shell.ChromeHasFocus() && _shell.Router.Current.Panel == ShellPanel.Launcher) _shell.Router.Close(); }
}
