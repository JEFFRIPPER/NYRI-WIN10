using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nyri.Win10.Models;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

public partial class ClipboardWindow : Window
{
    private readonly ShellCoordinator _shell;
    private bool _closed;

    public ClipboardWindow(ShellCoordinator shell)
    {
        _shell = shell;
        InitializeComponent();
        _shell.Runtime.ClipboardHistory.Changed += History_Changed;
        Closed += Window_Closed;
        Refresh();
    }

    // Only production routing calls this. Construction and headless layout do not show a window.
    public void Reveal()
    {
        Position();
        Show();
        Activate();
        Refresh();
        SearchBox.Focus();
    }

    private void Position()
    {
        var work = SystemParameters.WorkArea;
        Width = Math.Min(620, work.Width - 32);
        Height = Math.Min(560, work.Height - 32);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + Math.Min(70, Math.Max(12, (work.Height - Height) / 3));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Position();
        Refresh();
        SearchBox.Focus();
    }

    private void History_Changed(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => { if (!_closed) Refresh(); });
            return;
        }
        Refresh();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (EntriesList is not null) Refresh();
    }

    public void Refresh()
    {
        var history = _shell.Runtime.ClipboardHistory;
        var selectedId = EntriesList.SelectedItem is ClipboardEntry selected ? selected.Id : (Guid?)null;
        var query = SearchBox.Text.Trim();
        var entries = history.Enabled
            ? history.Entries.Where(entry => entry.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray()
            : Array.Empty<ClipboardEntry>();

        SearchBox.IsEnabled = history.Enabled;
        ClearButton.IsEnabled = history.Enabled && history.Entries.Count > 0;
        DisabledPane.Visibility = history.Enabled ? Visibility.Collapsed : Visibility.Visible;
        EmptyPane.Visibility = history.Enabled && entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EntriesContent.Visibility = history.Enabled && entries.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitleText.Text = query.Length == 0 ? "История пока пуста" : "Ничего не найдено";
        EmptyDetailText.Text = query.Length == 0
            ? "Скопируй текст в любом приложении — он появится здесь."
            : "Попробуй другое слово или очисти поиск.";
        CountText.Text = !history.Enabled ? "История отключена" : query.Length == 0
            ? $"Записей: {history.Entries.Count} · только в памяти"
            : $"Найдено: {entries.Length} из {history.Entries.Count}";
        EntriesList.ItemsSource = entries;
        EntriesList.SelectedItem = entries.FirstOrDefault(entry => entry.Id == selectedId) ?? entries.FirstOrDefault();
        UpdateSelection();
        SetStatus(history.Enabled ? "Ctrl+Enter — копировать · Del — удалить запись · Esc — закрыть" : "История отключена.");
    }

    private void Entries_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetailTextBox is not null) UpdateSelection();
    }

    private void UpdateSelection()
    {
        var entry = EntriesList.SelectedItem as ClipboardEntry;
        DetailTextBox.Text = entry?.Text ?? "";
        CopyButton.IsEnabled = RemoveButton.IsEnabled = _shell.Runtime.ClipboardHistory.Enabled && entry is not null;
    }

    private void Enable_Click(object sender, RoutedEventArgs e)
    {
        _shell.Runtime.Settings.Update(saved => saved with { ClipboardHistoryEnabled = true });
        _shell.Runtime.ClipboardHistory.SetEnabled(true);
        Refresh();
        SearchBox.Focus();
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopySelected();
    private void CopySelected()
    {
        if (!_shell.Runtime.ClipboardHistory.Enabled || EntriesList.SelectedItem is not ClipboardEntry entry) return;
        try
        {
            var data = new DataObject();
            data.SetText(entry.Text, TextDataFormat.UnicodeText);
            // History is session-only: copying from it must not reintroduce the
            // retained text into Windows' persistent/cloud clipboard history.
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]), autoConvert: false);
            Clipboard.SetDataObject(data, copy: true);
            SetStatus("Текст скопирован.");
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            SetStatus("Буфер обмена занят. Попробуй ещё раз.", error: true);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => RemoveSelected();
    private void RemoveSelected()
    {
        if (EntriesList.SelectedItem is not ClipboardEntry entry) return;
        _shell.Runtime.ClipboardHistory.Remove(entry.Id);
        SetStatus("Запись удалена.");
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _shell.Runtime.ClipboardHistory.Clear();
        SetStatus("История очищена.");
    }

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorBrush" : "MutedBrush");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => _shell.Router.Close();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _shell.Router.Close(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { CopySelected(); e.Handled = true; }
        else if (e.Key == Key.Delete && !SearchBox.IsKeyboardFocusWithin) { RemoveSelected(); e.Handled = true; }
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible && !_shell.ChromeHasFocus() && _shell.Router.Current.Panel == ShellPanel.Clipboard)
            _shell.Router.Close();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _shell.Runtime.ClipboardHistory.Changed -= History_Changed;
    }
}
