using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Threading;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

public sealed class WindowTrackerService : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectUncloaked = 0x8018;
    private const long WsChild = 0x40000000;
    private const long WsExToolWindow = 0x80;
    private const long WsExAppWindow = 0x40000;
    private const long WsExNoActivate = 0x08000000;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly WinEventProc _eventCallback;
    private readonly List<IntPtr> _hooks = [];
    private readonly Dictionary<(IntPtr Handle, uint ProcessId), ImageSource?> _icons = [];
    private readonly HashSet<(IntPtr Handle, uint ProcessId)> _pendingIcons = [];
    private readonly ConcurrentQueue<(IntPtr Handle, uint ProcessId)> _iconQueue = new();
    private readonly CancellationTokenSource _iconLifetime = new();
    private readonly CancellationToken _iconToken;
    private GCHandle _callbackRoot;
    private int _iconWorkerRunning;
    private volatile bool _disposed;

    public ObservableCollection<TrackedWindow> Windows { get; } = [];
    public string CurrentTitle { get; private set; } = "Рабочий стол";
    public event EventHandler? Changed;

    public WindowTrackerService(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _dispatcher.VerifyAccess();
        _iconToken = _iconLifetime.Token;
        _eventCallback = OnWindowEvent;
        _callbackRoot = GCHandle.Alloc(_eventCallback);
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _refreshTimer.Tick += OnRefreshTick;

        // Out-of-context hooks need this dispatcher's message loop and remain rooted
        // until UnhookWinEvent runs on the same thread during disposal.
        AddHook(EventSystemForeground, EventSystemForeground);
        AddHook(EventSystemMinimizeStart, EventSystemMinimizeEnd);
        AddHook(EventObjectCreate, EventObjectUncloaked);
        Refresh();
    }

    public bool Activate(TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_disposed || !IsWindow(window.Handle))
            return false;
        if (IsIconic(window.Handle))
            ShowWindowAsync(window.Handle, 9); // SW_RESTORE
        var activated = SetForegroundWindow(window.Handle);
        QueueRefresh();
        return activated;
    }

    private void AddHook(uint firstEvent, uint lastEvent)
    {
        const uint winEventOutOfContextSkipOwnProcess = 0x0002;
        var hook = SetWinEventHook(firstEvent, lastEvent, IntPtr.Zero,
            _eventCallback, 0, 0, winEventOutOfContextSkipOwnProcess);
        if (hook != IntPtr.Zero)
            _hooks.Add(hook);
        else
            AppDiagnostics.Write($"Window event hook {firstEvent:X}-{lastEvent:X} unavailable: {Marshal.GetLastWin32Error()}");
    }

    private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr hwnd,
        int objectId, int childId, uint eventThread, uint eventTime)
    {
        // Native callbacks only schedule work. Never query processes, windows or UI
        // from a reentrant hook, and ignore high-volume child/control events.
        if (_disposed || hwnd == IntPtr.Zero)
            return;
        if (eventType >= EventObjectCreate && (objectId != 0 || childId != 0 ||
            eventType is not (0x8000 or 0x8001 or 0x8002 or 0x8003 or 0x800A or 0x800C or 0x800F or 0x8017 or 0x8018)))
            return;
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            return;
        // Hook callbacks run on the registration thread. Activate can also be called
        // from another thread, so route the coalescing timer through the dispatcher.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(QueueRefresh));
            return;
        }
        // Do not restart this one-shot timer for every event: an application updating
        // its title continuously must not starve the refresh indefinitely.
        if (!_refreshTimer.IsEnabled)
            _refreshTimer.Start();
    }

    private void OnRefreshTick(object? sender, EventArgs args)
    {
        _refreshTimer.Stop();
        if (!_disposed)
            Refresh();
    }

    private void Refresh()
    {
        _dispatcher.VerifyAccess();
        var foreground = GetForegroundWindow();
        var snapshot = new List<TrackedWindow>();
        var processNames = new Dictionary<uint, string>();
        EnumWindows((hwnd, _) =>
        {
            var window = ReadWindow(hwnd, foreground, processNames);
            if (window is not null)
                snapshot.Add(window);
            return true;
        }, IntPtr.Zero);

        var identities = new HashSet<(IntPtr Handle, uint ProcessId)>();
        for (var index = 0; index < snapshot.Count; index++)
        {
            var window = snapshot[index];
            GetWindowThreadProcessId(window.Handle, out var processId);
            if (processId == 0) continue;
            var identity = (window.Handle, processId);
            identities.Add(identity);
            if (_icons.TryGetValue(identity, out var icon))
                snapshot[index] = window with { Icon = icon };
            else if (_pendingIcons.Add(identity))
                _iconQueue.Enqueue(identity);
        }
        foreach (var identity in _icons.Keys.Where(identity => !identities.Contains(identity)).ToArray())
            _icons.Remove(identity);
        StartIconWorker();

        // Preserve stable dock order and existing items while applying only changes.
        var byHandle = snapshot.ToDictionary(window => window.Handle);
        var changed = false;
        for (var index = Windows.Count - 1; index >= 0; index--)
        {
            if (!byHandle.ContainsKey(Windows[index].Handle))
            {
                Windows.RemoveAt(index);
                changed = true;
            }
        }
        foreach (var window in snapshot)
        {
            var index = FindIndex(window.Handle);
            if (index < 0)
            {
                Windows.Add(window);
                changed = true;
            }
            else if (Windows[index] != window)
            {
                Windows[index] = window;
                changed = true;
            }
        }

        // Opening Nyri's launcher should keep the previous external window title.
        GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        if (foregroundProcessId != (uint)Environment.ProcessId)
        {
            var current = snapshot.FirstOrDefault(window => window.Handle == foreground);
            var title = current?.Title ?? "Рабочий стол";
            if (CurrentTitle != title)
            {
                CurrentTitle = title;
                changed = true;
            }
        }
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private int FindIndex(IntPtr hwnd)
    {
        for (var index = 0; index < Windows.Count; index++)
            if (Windows[index].Handle == hwnd)
                return index;
        return -1;
    }

    private void StartIconWorker()
    {
        if (_disposed || _iconQueue.IsEmpty || Interlocked.CompareExchange(ref _iconWorkerRunning, 1, 0) != 0)
            return;
        _ = Task.Run(ReadQueuedIcons);
    }

    private void ReadQueuedIcons()
    {
        try
        {
            while (!_iconToken.IsCancellationRequested && _iconQueue.TryDequeue(out var identity))
            {
                GetWindowThreadProcessId(identity.Handle, out var processId);
                var icon = processId == identity.ProcessId ? WindowIconReader.Read(identity.Handle, _iconToken) : null;
                if (_iconToken.IsCancellationRequested || _dispatcher.HasShutdownStarted)
                    break;
                try
                {
                    _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        if (_disposed) return;
                        _pendingIcons.Remove(identity);
                        GetWindowThreadProcessId(identity.Handle, out var currentProcessId);
                        var index = FindIndex(identity.Handle);
                        if (index < 0 || currentProcessId != identity.ProcessId) return;
                        _icons[identity] = icon;
                        if (icon is null) return;
                        Windows[index] = Windows[index] with { Icon = icon };
                        Changed?.Invoke(this, EventArgs.Empty);
                    }));
                }
                catch (InvalidOperationException) { break; }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _iconWorkerRunning, 0);
            // Close the empty-queue race if an event enqueued another window just
            // before this worker returned. Only one background reader is active.
            StartIconWorker();
        }
    }

    private static TrackedWindow? ReadWindow(IntPtr hwnd, IntPtr foreground,
        Dictionary<uint, string> processNames)
    {
        if (!IsWindowVisible(hwnd))
            return null;
        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0 || processId == (uint)Environment.ProcessId)
            return null;

        var style = ReadWindowLong(hwnd, -16);
        var extendedStyle = ReadWindowLong(hwnd, -20);
        var isAppWindow = (extendedStyle & WsExAppWindow) != 0;
        if ((style & WsChild) != 0 || (!isAppWindow &&
                (extendedStyle & (WsExToolWindow | WsExNoActivate)) != 0))
            return null;
        if (DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            return null;

        var className = new StringBuilder(256);
        GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return null;

        var owner = GetWindow(hwnd, 4); // GW_OWNER
        // A modal dialog is useful when active or when its owner is hidden. Ordinary
        // owned auxiliaries otherwise share the owner's single dock entry.
        if (owner != IntPtr.Zero && !isAppWindow && hwnd != foreground && IsWindowVisible(owner))
            return null;

        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return null;
        var title = new StringBuilder(Math.Min(length + 1, 4096));
        GetWindowText(hwnd, title, title.Capacity);
        var text = title.ToString().Trim();
        if (text.Length == 0)
            return null;

        if (!processNames.TryGetValue(processId, out var processName))
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                processName = $"PID {processId}";
            }
            processNames[processId] = processName;
        }
        return new TrackedWindow(hwnd, text, processName);
    }

    private static long ReadWindowLong(IntPtr hwnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, index).ToInt64() : GetWindowLong(hwnd, index);

    public void Dispose()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Dispose);
            return;
        }
        if (_disposed)
            return;
        _disposed = true;
        _iconLifetime.Cancel();
        _iconQueue.Clear();
        _icons.Clear();
        _pendingIcons.Clear();
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTick;
        foreach (var hook in _hooks)
            UnhookWinEvent(hook);
        _hooks.Clear();
        if (_callbackRoot.IsAllocated)
            _callbackRoot.Free();
        _iconLifetime.Dispose();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd,
        int objectId, int childId, uint eventThread, uint eventTime);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProc callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute,
        out int value, int size);
}
