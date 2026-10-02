using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Nyri.Win10.Services;

/// <summary>Samples native system resource counters without inventing missing data.</summary>
public sealed class SystemResourceService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _ticker;
    private readonly HashSet<string> _reportedFailures = new(StringComparer.Ordinal);
    private CpuSample? _previousCpu;
    private ResourceSnapshot _snapshot = new();
    private volatile bool _disposed;

    public double? CpuPercent => _snapshot.CpuPercent;
    public double? MemoryPercent => _snapshot.MemoryPercent;
    public double? BatteryPercent => _snapshot.BatteryPercent;
    public bool? OnAC => _snapshot.OnAC;
    public bool BatteryAvailable => _snapshot.BatteryAvailable;
    public event EventHandler? Changed;

    public SystemResourceService(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _ticker = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _ticker.Tick += OnTick;
        if (_dispatcher.CheckAccess()) Start();
        else _dispatcher.Invoke(Start);
    }

    private void Start()
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        Refresh();
        _ticker.Start();
    }

    private void OnTick(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        if (_disposed) return;
        var cpu = ReadCpu();
        var memory = ReadMemory();
        var (battery, onAC, batteryAvailable) = ReadPower();
        var next = new ResourceSnapshot(cpu, memory, battery, onAC, batteryAvailable);
        if (_disposed || next == _snapshot) return;
        _snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private double? ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ReportFailure("CPU");
            _previousCpu = null;
            return null;
        }
        _reportedFailures.Remove("CPU");
        var current = new CpuSample(idle.Value, kernel.Value, user.Value);
        var previous = _previousCpu;
        _previousCpu = current;
        if (previous is null || current.Idle < previous.Value.Idle ||
            current.Kernel < previous.Value.Kernel || current.User < previous.Value.User) return null;
        // Kernel time already includes idle time. Work is kernel + user - idle.
        // On systems with >64 CPUs GetSystemTimes reports the current processor group.
        var total = (double)(current.Kernel - previous.Value.Kernel) + (current.User - previous.Value.User);
        var idleDelta = current.Idle - previous.Value.Idle;
        if (total <= 0 || idleDelta > total) return null;
        return 100 * (total - idleDelta) / total;
    }

    private double? ReadMemory()
    {
        var state = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref state))
        {
            ReportFailure("RAM");
            return null;
        }
        _reportedFailures.Remove("RAM");
        if (state.TotalPhysical == 0 || state.AvailablePhysical > state.TotalPhysical) return null;
        return 100d * (state.TotalPhysical - state.AvailablePhysical) / state.TotalPhysical;
    }

    private (double? Battery, bool? OnAC, bool Available) ReadPower()
    {
        if (!GetSystemPowerStatus(out var state))
        {
            ReportFailure("Power");
            return (null, null, false);
        }
        _reportedFailures.Remove("Power");
        bool? onAC = state.ACLineStatus switch { 0 => false, 1 => true, _ => null };
        var available = state.BatteryFlag != 255 && (state.BatteryFlag & 128) == 0;
        double? percent = available && state.BatteryLifePercent <= 100 ? state.BatteryLifePercent : null;
        return (percent, onAC, available);
    }

    private void ReportFailure(string counter)
    {
        if (_reportedFailures.Add(counter))
            AppDiagnostics.Write($"System resource {counter} unavailable: win32={Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_dispatcher.CheckAccess() && !_dispatcher.HasShutdownStarted) _dispatcher.Invoke(Stop);
        else Stop();
    }

    private void Stop()
    {
        _ticker.Stop();
        _ticker.Tick -= OnTick;
        Changed = null;
    }

    private sealed record ResourceSnapshot(double? CpuPercent = null, double? MemoryPercent = null,
        double? BatteryPercent = null, bool? OnAC = null, bool BatteryAvailable = false);
    private readonly record struct CpuSample(ulong Idle, ulong Kernel, ulong User);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    // Microsoft native contracts:
    // https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes
    // https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex
    // https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getsystempowerstatus
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus state);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out PowerStatus state);
}
