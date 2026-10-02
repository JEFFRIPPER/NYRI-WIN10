using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

public sealed class ApplicationCatalogService
{
    private readonly string[] _roots;

    public ApplicationCatalogService(IEnumerable<string>? roots = null)
    {
        _roots = (roots ?? new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        }).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
    }

    public Task<IReadOnlyList<LaunchableApp>> LoadAsync(CancellationToken token = default) =>
        Task.Run<IReadOnlyList<LaunchableApp>>(() => Load(token), token);

    public bool Launch(LaunchableApp app, out string? error)
    {
        ArgumentNullException.ThrowIfNull(app);
        try
        {
            // Let Windows resolve shortcut targets and registered URI handlers.
            // Passing a filename directly avoids interpreting its contents as a command.
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = app.LaunchPath,
                UseShellExecute = true
            });
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or
            InvalidOperationException or ArgumentException or NotSupportedException or SecurityException)
        {
            error = $"Не удалось открыть «{app.Name}»: {exception.Message}";
            return false;
        }
    }

    private IReadOnlyList<LaunchableApp> Load(CancellationToken token)
    {
        var apps = new Dictionary<string, LaunchableApp>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in _roots)
        {
            token.ThrowIfCancellationRequested();
            foreach (var path in EnumerateShortcuts(root, visited, token))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(path).Trim();
                if (name.Length == 0 || apps.ContainsKey(name))
                    continue;
                apps.Add(name, new LaunchableApp($"shortcut:{path}", name, path,
                    ShellIconReader.Read(path), Description: "Меню «Пуск»"));
            }
        }

        token.ThrowIfCancellationRequested();
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (File.Exists(explorer))
            apps.TryAdd("Проводник", new LaunchableApp("windows:explorer", "Проводник", explorer,
                ShellIconReader.Read(explorer), "\uE8B7", "Файлы и папки"));
        apps.TryAdd("Параметры Windows", new LaunchableApp("windows:settings", "Параметры Windows",
            "ms-settings:", Glyph: "\uE713", Description: "Системные настройки"));
        token.ThrowIfCancellationRequested();
        return apps.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static IEnumerable<string> EnumerateShortcuts(string root, HashSet<string> visited,
        CancellationToken token)
    {
        string fullRoot;
        try
        {
            fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
                yield break;
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(fullRoot);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (!visited.Add(directory))
                continue;

            string[] entries;
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    continue;
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch (Exception exception) when (IsFileSystemFailure(exception))
            {
                // An inaccessible folder must not prevent loading the remaining menu.
                continue;
            }

            Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception exception) when (IsFileSystemFailure(exception)) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                var extension = Path.GetExtension(entry);
                if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                    yield return entry;
            }
        }
    }

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
