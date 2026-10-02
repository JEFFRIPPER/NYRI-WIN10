using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nyri.Win10;
using Nyri.Win10.Services;
using Nyri.Win10.Shell;

internal static class UiChecks
{
    public static void Run(Action<bool, string> check, string[] args)
    {
        var clipboardOnly = args.Contains("--clipboard-ui");
        var app = new App();
        app.InitializeComponent();
        var theme = new ThemeService(app.Resources);
        var settingsDirectory = Path.Combine(Path.GetTempPath(), "NyriUiCheck-" + Guid.NewGuid());
        using var runtime = new ShellRuntime(startSystemProviders: false, settings: new SettingsService(settingsDirectory));
        using var shell = new ShellCoordinator(runtime, theme);
        var launcher = new LauncherWindow(shell);
        if (!clipboardOnly) CatalogChecks.RunLauncherChecks(launcher, check);
        var control = new ControlCenterWindow(shell);
        var settings = new SettingsWindow(shell);
        var live = new MainWindow(runtime);
        var wallpaper = new WallpaperWindow(shell);
        var clipboard = new ClipboardWindow(shell);
        runtime.ClipboardHistory.SetEnabled(true);
        runtime.ClipboardHistory.Add("Пример заметки\nВторая строка остаётся на месте.");
        runtime.ClipboardHistory.Add("Поиск находит текст внутри любой строки записи.");
        var clipboardSearch = (TextBox)clipboard.FindName("SearchBox");
        var clipboardList = (ListBox)clipboard.FindName("EntriesList");
        clipboardSearch.Text = "Вторая строка";
        check(clipboardList.Items.Count == 1, "Clipboard panel searches the complete multiline value without reading Windows clipboard");
        clipboardSearch.Clear();
        check(clipboardList.Items.Count == 2, "Clipboard panel receives shared history events without showing a window");
        check(((CheckBox)settings.FindName("ClipboardCheck")).IsChecked == true,
            "Settings reflect a history toggle made from another shell surface");
        using var resources = new SystemResourceService(app.Dispatcher);
        var leftWidgets = new DesktopWidgetWindow(shell, resources, rightSide: false);
        var rightWidgets = new DesktopWidgetWindow(shell, resources, rightSide: true);
        Window[] windows = args.Contains("--clipboard-ui")
            ? [clipboard, settings]
            : [shell.Bar, shell.Dock, launcher, control, settings, live, wallpaper, leftWidgets, rightWidgets, clipboard];
        try
        {
            if (clipboardOnly)
            {
                theme.Apply(false, "ocean");
                check(Equals(((ComboBox)settings.FindName("PaletteBox")).SelectedValue, "ocean") && !File.Exists(Path.Combine(settingsDirectory, "settings.json")),
                    "Shared theme changes update settings controls without silently rewriting preferences");
                theme.Apply(true, "terracotta");
            }
            else
            {
            var originalInk = ((SolidColorBrush)settings.Foreground).Color;
            theme.Apply(false, "ocean");
            check(((SolidColorBrush)settings.Foreground).Color != originalInk,
                "Palette changes reach an existing shell window through dynamic resources");
            var button = new Button { Content = "Test", Style = (Style)app.FindResource("FilledButtonStyle") };
            button.ApplyTemplate();
            check(Equals(button.Foreground, app.FindResource("OnPrimaryBrush")),
                "Filled buttons use the corresponding on-primary ink role");
            theme.Apply(true, "terracotta");
            var wallpaperService = new WallpaperService();
            var bauhaus = wallpaperService.Preview("bauhaus", "terracotta", 128, 80);
            var waves = wallpaperService.Preview("waves", "purple", 128, 80);
            check(bauhaus.IsFrozen && waves.IsFrozen && bauhaus.PixelWidth == 128 && waves.PixelHeight == 80,
                "Both wallpaper styles render immutable previews without changing Windows wallpaper");
            var pixelsA = new byte[128 * 80 * 4]; var pixelsB = new byte[pixelsA.Length];
            bauhaus.CopyPixels(pixelsA, 128 * 4, 0); waves.CopyPixels(pixelsB, 128 * 4, 0);
            check(!pixelsA.SequenceEqual(pixelsB), "Wallpaper selection produces distinct actual preview images");
            check(resources.MemoryPercent is null or >= 0 and <= 100 && resources.CpuPercent is null or >= 0 and <= 100,
                "Desktop resource readings are normalized or explicitly unavailable");
            }
            var outputIndex = Array.IndexOf(args, "--render-ui");
            var outputDirectory = outputIndex >= 0 && outputIndex + 1 < args.Length ? Path.GetFullPath(args[outputIndex + 1]) : null;
            if (outputDirectory is not null) Directory.CreateDirectory(outputDirectory);
            foreach (var window in windows)
            {
                check(!window.IsVisible, window.GetType().Name + " remains hidden during preview checks");
                var content = (FrameworkElement)window.Content;
                var width = window == shell.Bar ? 1280 : window == shell.Dock ? 500 : window.Width;
                var height = window == shell.Dock ? 76 : double.IsNaN(window.Height) ? 480 : window.Height;
                content.Measure(new Size(width, height));
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();
                check(content.ActualWidth > 0 && content.ActualHeight > 0,
                    window.GetType().Name + " loads themed XAML and completes layout without a native window");
                if (outputDirectory is not null)
                {
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    var name = window == leftWidgets ? "DesktopWidgetsLeft" : window == rightWidgets ? "DesktopWidgetsRight" : window.GetType().Name;
                    using var output = File.Create(Path.Combine(outputDirectory, name + ".png"));
                    encoder.Save(output);
                }
            }
        }
        finally
        {
            launcher.Close(); control.Close(); settings.Close(); live.Close();
            wallpaper.Close(); leftWidgets.Close(); rightWidgets.Close();
            clipboard.Close();
            if (Directory.Exists(settingsDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(settingsDirectory)) File.Delete(file);
                Directory.Delete(settingsDirectory);
            }
        }
    }
}
