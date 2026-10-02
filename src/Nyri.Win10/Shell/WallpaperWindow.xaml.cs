using System.Windows;
using System.Windows.Controls;
using Nyri.Win10.Services;

namespace Nyri.Win10.Shell;

public partial class WallpaperWindow : Window
{
    private readonly ShellCoordinator _shell;
    private readonly WallpaperService _wallpapers = new();
    private string _style;
    private string _palette;
    public WallpaperWindow(ShellCoordinator shell)
    {
        _shell = shell;
        var saved = shell.Runtime.Settings.Load();
        _style = saved.WallpaperStyle;
        _palette = saved.Palette;
        InitializeComponent();
        Refresh();
    }
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(600, work.Width - 24);
        MinHeight = Math.Min(480, work.Height - 24);
        Width = Math.Min(920, work.Width - 24);
        Height = Math.Min(700, work.Height - 24);
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Top + (work.Height - Height) / 2;
    }
    private void Refresh()
    {
        PreviewImage.Source = _wallpapers.Preview(_style, _palette, 960, 540);
        SelectionText.Text = (_style == "waves" ? "Волны" : "Bauhaus") + " · " + (_palette == "purple" ? "Сирень" : _palette == "ocean" ? "Океан" : "Терракота");
    }
    private void Style_Click(object sender, RoutedEventArgs e) { _style = (string)((Button)sender).Tag; Refresh(); }
    private void Palette_Click(object sender, RoutedEventArgs e) { _palette = (string)((Button)sender).Tag; Refresh(); }
    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _wallpapers.Apply(_style, _palette);
            _shell.Theme.Apply(_shell.Theme.Dark, _palette);
            _shell.Runtime.Settings.Update(saved => saved with { WallpaperStyle = _style, Palette = _palette });
            StatusText.Text = "Обои и палитра применены";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось применить обои. Windows может запрещать их изменение.";
            AppDiagnostics.Write("Wallpaper application failed: " + ex.GetType().Name);
        }
    }
}
