using System.Reflection;
using System.Windows.Threading;
using Microsoft.Win32;
using Nyri.Win10.Models;
using Nyri.Win10.Services;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
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
    }

    private static void Check(bool result, string message)
    {
        if (!result) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}
