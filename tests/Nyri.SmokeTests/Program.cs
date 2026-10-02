using System.Reflection;
using System.Windows.Threading;
using Microsoft.Win32;
using Nyri.Win10.Models;
using Nyri.Win10.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--clipboard-only"))
        {
            ClipboardHistoryChecks.Run(Check);
            ClipboardPolicyChecks.Run(Check);
            return;
        }
        if (args.Contains("--ui-only") || args.Contains("--clipboard-ui")) { UiChecks.Run(Check, args); return; }
        using (var primary = new SingleInstanceService(@"Local\NyriSmokeTest." + Guid.NewGuid()))
        {
            // The service identity is tested below with another independent owner.
            Check(primary.IsPrimary, "First launch owns the single-instance guard");
        }
        var identity = @"Local\NyriSmokeTest." + Guid.NewGuid();
        using (var primary = new SingleInstanceService(identity))
        using (var activated = new ManualResetEventSlim())
        {
            primary.StartListening(() => activated.Set());
            var signalled = Task.Run(() =>
            {
                using var secondary = new SingleInstanceService(identity);
                Check(!secondary.IsPrimary, "Second launch does not acquire ownership");
                return secondary.SignalPrimary();
            }).GetAwaiter().GetResult();
            Check(signalled && activated.Wait(TimeSpan.FromSeconds(5)), "Second launch activates the first instance");
        }
        using (var restarted = new SingleInstanceService(identity))
            Check(restarted.IsPrimary, "Guard is released on shutdown");
        using (var runtime = new ShellRuntime(startSystemProviders: false))
        {
            var start = runtime.StartAsync();
            Check(ReferenceEquals(start, runtime.StartAsync()), "Shell runtime starts providers only once");
            start.GetAwaiter().GetResult();
            runtime.PublishClipboard("first");
            runtime.PublishClipboard("latest");
            Check(runtime.Hub.Activities.Single(x => x.Id == "clipboard").Detail == "latest", "Shared clipboard state replaces the previous event");
            runtime.Timers.StartCountdown(TimeSpan.FromMinutes(5));
            Check(runtime.Hub.Activities.Any(x => x.Id == "timer"), "Timer publishes into the shared shell hub");
            runtime.Dispose();
            var count = runtime.Hub.Activities.Count;
            runtime.PublishClipboard("after shutdown");
            runtime.Hub.Upsert(new IslandActivity("late", IslandActivityKind.System, "late", "", "", DateTimeOffset.Now));
            Check(runtime.IsDisposed && runtime.Hub.Activities.Count == count, "Shutdown prevents late provider mutations");
        }
        using (var router = new PanelRouter())
        {
            router.Open(ShellPanel.Launcher);
            router.Open(ShellPanel.ControlCenter, "privacy");
            Check(router.Current.Panel == ShellPanel.ControlCenter && router.Current.Page == "privacy", "Panel navigation replaces the prior transient route");
            router.Toggle(ShellPanel.ControlCenter, "privacy");
            Check(router.Current.Panel == ShellPanel.None, "Repeating the current route closes its panel");
        }
        var settingsDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NyriSettingsTest-" + Guid.NewGuid());
        try
        {
            var settings = new SettingsService(settingsDirectory);
            settings.Save(new AppSettings(20, 30, "bottom", false, false, "ocean"));
            settings.Update(saved => saved with { Left = null, Top = null });
            var restored = settings.Load();
            Check(restored.BarPosition == "bottom" && !restored.DockEnabled && !restored.Dark && restored.Palette == "ocean", "Resetting island position preserves shell preferences");
        }
        finally { if (System.IO.Directory.Exists(settingsDirectory)) { foreach (var file in System.IO.Directory.EnumerateFiles(settingsDirectory)) System.IO.File.Delete(file); System.IO.Directory.Delete(settingsDirectory); } }
        var hub = new ActivityHub();
        var privacy = new IslandActivity("privacy:microphone", IslandActivityKind.Microphone,
            "Mic", "test", "M", DateTimeOffset.Now, Priority: 100);
        hub.Upsert(privacy);
        for (var i = 0; i < 12; i++) hub.Upsert(privacy with { Id = "other:" + i, Priority = i });
        Check(hub.Primary?.Id == privacy.Id, "Active privacy survives more than eight activities");
        var notifications = 0;
        hub.Changed += (_, _) => notifications++;
        hub.Upsert(privacy with { UpdatedAt = DateTimeOffset.Now.AddSeconds(1) });
        Check(notifications == 0, "Identical reconciliation does not notify");
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() => hub.Remove(privacy.Id)).GetAwaiter().GetResult();
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        Check(hub.Activities.All(x => x.Id != privacy.Id), "Background removal reaches UI dispatcher");

        var path = @"Software\NyriSmokeTests\" + Guid.NewGuid();
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(path);
            using var child = root.CreateSubKey("capture.exe");
            var read = typeof(PrivacyService).GetMethod("ReadActive", BindingFlags.NonPublic | BindingFlags.Static)!;
            child.SetValue("LastUsedTimeStart", 123L, RegistryValueKind.QWord);
            child.SetValue("LastUsedTimeStop", 0L, RegistryValueKind.QWord);
            var apps = new List<string>();
            read.Invoke(null, [root, apps]);
            Check(apps.SequenceEqual(["capture.exe"]), "Active capture detected recursively");
            child.SetValue("LastUsedTimeStop", 456L, RegistryValueKind.QWord);
            apps.Clear();
            read.Invoke(null, [root, apps]);
            Check(apps.Count == 0, "Stopped capture is not reported active");
            child.DeleteValue("LastUsedTimeStop");
            read.Invoke(null, [root, apps]);
            Check(apps.Count == 0, "Missing usage metadata is not reported active");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        CatalogChecks.Run(Check);
        ClipboardHistoryChecks.Run(Check);
        ClipboardPolicyChecks.Run(Check);
        if (args.Contains("--check-audio")) AudioChecks.Run(Check);
        UiChecks.Run(Check, args);
    }

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
