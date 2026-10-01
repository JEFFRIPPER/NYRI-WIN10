using System.IO;

namespace Nyri.Win10.Services;

public static class AppDiagnostics
{
    public static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NYRI-WIN10");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}{Environment.NewLine}");
        }
        catch { /* Diagnostics must not interrupt startup or shutdown. */ }
    }
}
