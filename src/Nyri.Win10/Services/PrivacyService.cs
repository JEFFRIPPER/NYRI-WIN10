using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

/// <summary>Observes Windows privacy usage metadata without opening capture devices.</summary>
public sealed class PrivacyService : IDisposable
{
    private const string ConsentPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private readonly ActivityHub _hub;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _watch;
    private bool _disposed;

    public PrivacyService(ActivityHub hub)
    {
        _hub = hub;
        _watch = Task.Run(Watch);
    }

    private void Watch()
    {
        using var changed = new AutoResetEvent(false);
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(ConsentPath);
                var watching = root is not null && RegNotifyChangeKeyValue(
                    root.Handle, true, 1 | 4 | 0x10000000, changed.SafeWaitHandle.DangerousGetHandle(), true) == 0;
                if (root is null) PublishUnavailable();
                Refresh(root, "microphone", IslandActivityKind.Microphone, "Микрофон используется", "\uE720");
                Refresh(root, "webcam", IslandActivityKind.Camera, "Камера используется", "\uE714");
                // Periodic reconciliation also handles keys created later and missed notifications.
                WaitHandle.WaitAny([_stop.Token.WaitHandle, changed], watching ? 10000 : 2000);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                PublishUnavailable();
                _stop.Token.WaitHandle.WaitOne(10000);
            }
        }
    }

    private void Refresh(RegistryKey? root, string capability, IslandActivityKind kind, string title, string glyph)
    {
        using var key = root?.OpenSubKey(capability);
        if (key is null)
        {
            _hub.Remove("privacy:" + capability);
            return;
        }
        var apps = new List<string>();
        ReadActive(key, apps);
        var id = "privacy:" + capability;
        if (apps.Count == 0) _hub.Remove(id);
        else _hub.Upsert(new IslandActivity(id, kind, title,
            string.Join(", ", apps.Distinct().OrderBy(x => x)), glyph, DateTimeOffset.Now, Priority: 100));
        _hub.Remove("privacy:unavailable");
    }

    private static void ReadActive(RegistryKey key, List<string> apps)
    {
        if (key.GetValue("LastUsedTimeStart") is long start && start > 0 &&
            key.GetValue("LastUsedTimeStop") is long stop && stop == 0)
        {
            var name = key.Name[(key.Name.LastIndexOf('\\') + 1)..].Replace('#', '\\');
            apps.Add(Path.GetFileName(name));
        }
        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name);
            if (child is not null) ReadActive(child, apps);
        }
    }

    private void PublishUnavailable() => _hub.Upsert(new IslandActivity(
        "privacy:unavailable", IslandActivityKind.System, "Privacy: нет данных",
        "Windows не предоставила сведения о микрофоне и камере", "!", DateTimeOffset.Now, Priority: 95));

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(Microsoft.Win32.SafeHandles.SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool subtree, uint filter, IntPtr signal,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _watch.GetAwaiter().GetResult();
        _stop.Dispose();
    }
}


