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
    string[]? PinnedApps = null
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
                Palette = settings.Palette is "purple" or "ocean" ? settings.Palette : "terracotta"
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
