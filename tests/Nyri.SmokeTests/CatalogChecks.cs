using System.IO;
using System.Reflection;
using System.Windows.Controls;
using Nyri.Win10.Services;
using Nyri.Win10.Shell;

internal static class CatalogChecks
{
    public static void Run(Action<bool, string> check)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "NyriCatalogTest-" + Guid.NewGuid().ToString("N"));
        var userMenu = Path.Combine(temporaryRoot, "user");
        var sharedMenu = Path.Combine(temporaryRoot, "shared");
        var nestedMenu = Path.Combine(userMenu, "nested");
        var userShortcut = Path.Combine(userMenu, "Example.url");
        var duplicateShortcut = Path.Combine(sharedMenu, "example.URL");
        var nestedShortcut = Path.Combine(nestedMenu, "Nested.URL");
        var ignoredFile = Path.Combine(userMenu, "Ignored.txt");
        var ignoredExecutable = Path.Combine(sharedMenu, "Ignored.exe");
        var existingFiles = new[] { userShortcut, duplicateShortcut, nestedShortcut, ignoredFile, ignoredExecutable };
        try
        {
            Directory.CreateDirectory(nestedMenu);
            Directory.CreateDirectory(sharedMenu);
            const string shortcut = "[InternetShortcut]\r\nURL=https://example.com/\r\n";
            File.WriteAllText(userShortcut, shortcut);
            File.WriteAllText(duplicateShortcut, shortcut);
            File.WriteAllText(nestedShortcut, shortcut);
            File.WriteAllText(ignoredFile, "Not a menu shortcut");
            File.WriteAllText(ignoredExecutable, "Not a menu shortcut");

            var catalog = new ApplicationCatalogService([
                userMenu, sharedMenu, userMenu, Path.Combine(temporaryRoot, "missing")
            ]);
            var apps = catalog.LoadAsync().GetAwaiter().GetResult();
            var menuEntries = apps.Where(app => app.Id.StartsWith("shortcut:", StringComparison.Ordinal)).ToArray();
            check(menuEntries.Length == 2, "Catalog scans nested roots once and ignores unsupported files");
            var duplicates = apps.Where(app => app.Name.Equals("Example", StringComparison.OrdinalIgnoreCase)).ToArray();
            check(duplicates.Length == 1 && duplicates[0].LaunchPath == userShortcut,
                "Catalog deduplicates names case-insensitively and prefers the first menu root");
            check(menuEntries.Any(app => app.Name == "Nested" && app.LaunchPath == nestedShortcut),
                "Catalog retains the real shortcut path from nested menu folders");
            check(apps.All(app => app.Icon is null || app.Icon.IsFrozen),
                "Background catalog icons are frozen for dispatcher use");
            check(apps.Select(app => app.Name).SequenceEqual(apps.Select(app => app.Name)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)),
                "Catalog returns entries in display-name order");

            var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            check(apps.Any(app => app.Id == "windows:explorer" && app.LaunchPath == explorerPath && File.Exists(app.LaunchPath)),
                "Catalog includes the existing Windows Explorer executable");
            check(apps.Any(app => app.Id == "windows:settings" && app.LaunchPath == "ms-settings:"),
                "Catalog includes the registered Windows Settings URI");

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var observedCancellation = false;
            try { catalog.LoadAsync(cancelled.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { observedCancellation = true; }
            check(observedCancellation, "Catalog respects cancellation before scanning");
        }
        finally
        {
            // Only remove paths created by this check, without traversing directories.
            foreach (var file in existingFiles)
                if (File.Exists(file)) File.Delete(file);
            foreach (var directory in new[] { nestedMenu, userMenu, sharedMenu, temporaryRoot })
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: false);
        }

        var parser = typeof(LauncherWindow).GetMethod("TryTimer", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("Launcher timer parser");
        (bool Accepted, TimeSpan Duration) Parse(string query)
        {
            object?[] arguments = [query, TimeSpan.Zero];
            var accepted = (bool)parser.Invoke(null, arguments)!;
            return (accepted, (TimeSpan)arguments[1]!);
        }

        var normal = Parse("таймер 5м");
        check(normal.Accepted && normal.Duration == TimeSpan.FromMinutes(5), "Launcher command creates a five-minute countdown");
        var fraction = Parse("timer 1,5m");
        check(fraction.Accepted && fraction.Duration == TimeSpan.FromSeconds(90), "Launcher accepts decimal-comma timer durations");
        var day = Parse("таймер 24ч");
        check(day.Accepted && day.Duration == TimeSpan.FromDays(1), "Launcher accepts a full-day timer without losing the day");
        var maximum = Parse("timer 168h");
        check(maximum.Accepted && maximum.Duration == TimeSpan.FromDays(7), "Launcher accepts the seven-day duration boundary");
        var oneSecond = Parse("timer 1s");
        check(oneSecond.Accepted && oneSecond.Duration == TimeSpan.FromSeconds(1), "Launcher accepts the one-second duration boundary");
        check(new[] { "таймер 0.000000001с", "timer 0.99s", "таймер 0м", "timer -1m", "timer 168.01h",
                "timer 999999999999999999999999h", "timer NaNs", "timer Infinitys", "timer 5x", "timer м", "timer", "", "timer 5m extra" }
                .All(query => !Parse(query).Accepted),
            "Launcher rejects sub-second, excessive, non-finite, negative and malformed durations");
    }

    public static void RunLauncherChecks(LauncherWindow launcher, Action<bool, string> check)
    {
        var search = (TextBox)launcher.FindName("SearchBox");
        var results = (ListBox)launcher.FindName("Results");
        var originalQuery = search.Text;
        try
        {
            search.Text = "таймер 24ч";
            launcher.Refresh();
            var result = results.Items.OfType<SearchResult>().SingleOrDefault(item => item.Timer == TimeSpan.FromDays(1));
            check(result?.Name == "Таймер 1.00:00:00", "Launcher displays the real full-day result label including its day count");
            search.Text = "таймер 0.000000001с";
            launcher.Refresh();
            check(results.Items.OfType<SearchResult>().All(item => item.Timer is null),
                "An underflowing timer command creates no actionable countdown result");
        }
        finally
        {
            search.Text = originalQuery;
            launcher.Refresh();
        }
    }
}
