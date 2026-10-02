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
        var app = new App();
        app.InitializeComponent();
        var theme = new ThemeService(app.Resources);
        using var runtime = new ShellRuntime(startSystemProviders: false);
        using var shell = new ShellCoordinator(runtime, theme);
        var launcher = new LauncherWindow(shell);
        CatalogChecks.RunLauncherChecks(launcher, check);
        var control = new ControlCenterWindow(shell);
        var settings = new SettingsWindow(shell);
        var live = new MainWindow(runtime);
        Window[] windows = [shell.Bar, shell.Dock, launcher, control, settings, live];
        try
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
            var outputIndex = Array.IndexOf(args, "--render-ui");
            var outputDirectory = outputIndex >= 0 && outputIndex + 1 < args.Length ? Path.GetFullPath(args[outputIndex + 1]) : null;
            if (outputDirectory is not null) Directory.CreateDirectory(outputDirectory);
            foreach (var window in windows)
            {
                var content = (FrameworkElement)window.Content;
                var width = window == shell.Bar ? 1280 : window == shell.Dock ? 500 : window.Width;
                var height = window == shell.Dock ? 76 : window.Height;
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
                    using var output = File.Create(Path.Combine(outputDirectory, window.GetType().Name + ".png"));
                    encoder.Save(output);
                }
            }
        }
        finally
        {
            launcher.Close(); control.Close(); settings.Close(); live.Close();
        }
    }
}
