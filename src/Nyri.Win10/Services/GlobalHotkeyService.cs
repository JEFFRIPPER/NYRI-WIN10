using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nyri.Win10.Services;

/// <summary>Registers shortcuts without replacing Windows' own reserved key combinations.</summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private readonly IntPtr _handle;
    private readonly HwndSource? _source;
    private readonly Dictionary<int, Action> _actions = new();
    private bool _disposed;

    public GlobalHotkeyService(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(OnMessage);
    }

    public bool Register(int id, uint key, Action action)
    {
        if (_disposed) return false;
        const uint controlAltNoRepeat = 0x0002 | 0x0001 | 0x4000;
        if (!RegisterHotKey(_handle, id, controlAltNoRepeat, key))
        {
            AppDiagnostics.Write($"Shortcut {id} is unavailable");
            return false;
        }
        _actions[id] = action;
        return true;
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && !_disposed && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _actions.Keys) UnregisterHotKey(_handle, id);
        _source?.RemoveHook(OnMessage);
        _actions.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
