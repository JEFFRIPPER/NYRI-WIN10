using System.IO;
using System.Text.Json;

namespace Nyri.Win10.Services;

public sealed record AppSettings(
    double? Left = null,
    double? Top = null
);

public sealed class SettingsService
{
    private readonly string _directory;
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NYRI-WIN10");
        _path = Path.Combine(_directory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppSettings();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, _json)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, _json));
            File.Move(temp, _path, true);
        }
        catch
        {
            // Settings failure must never crash the shell.
        }
    }
}