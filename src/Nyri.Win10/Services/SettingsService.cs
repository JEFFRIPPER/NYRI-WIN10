using System.IO;
using System.Text.Json;

namespace Nyri.Win10.Services;

public sealed record AppSettings(
    double? Left = null,
    double? Top = null,
    string BarPosition = "top",
    bool DockEnabled = true,
    bool Dark = true,
    string Palette = "terracotta",
    string[]? PinnedApps = null,
    bool DesktopWidgetsEnabled = true,
    string WallpaperStyle = "bauhaus",
    double? WidgetLeftX = null,
    double? WidgetLeftY = null,
    double? WidgetRightX = null,
    double? WidgetRightY = null
);

public sealed class SettingsService
{
    private readonly string _directory;
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public SettingsService(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NYRI-WIN10");
        _path = Path.Combine(_directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), _json) ?? new AppSettings();
            return settings with
            {
                Left = settings.Left is double x && double.IsFinite(x) ? x : null,
                Top = settings.Top is double y && double.IsFinite(y) ? y : null,
                BarPosition = settings.BarPosition == "bottom" ? "bottom" : "top",
                Palette = settings.Palette is "purple" or "ocean" ? settings.Palette : "terracotta",
                WallpaperStyle = settings.WallpaperStyle == "waves" ? "waves" : "bauhaus",
                WidgetLeftX = settings.WidgetLeftX is double lx && double.IsFinite(lx) ? lx : null,
                WidgetLeftY = settings.WidgetLeftY is double ly && double.IsFinite(ly) ? ly : null,
                WidgetRightX = settings.WidgetRightX is double rx && double.IsFinite(rx) ? rx : null,
                WidgetRightY = settings.WidgetRightY is double ry && double.IsFinite(ry) ? ry : null
            };
        }
        catch { return new AppSettings(); }
    }

    public void Update(Func<AppSettings, AppSettings> change) => Save(change(Load()));

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, _json));
            File.Move(temp, _path, true);
        }
        catch { AppDiagnostics.Write("Settings could not be saved"); }
    }
}
