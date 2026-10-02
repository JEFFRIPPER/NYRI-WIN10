using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nyri.Win10.Services;

/// <summary>Reserves the primary monitor edge and releases it when the shell exits.</summary>
public sealed class AppBarService : IDisposable
{
    private const int CallbackMessage = 0x800A;
    private readonly Window _window;
    private readonly IntPtr _handle;
    private readonly HwndSource? _source;
    private readonly double _height;
    private string _edge;
    private bool _registered;
    private bool _positioning;
    private bool _disposed;

    public AppBarService(Window window, double height, string edge)
    {
        _window = window;
        _height = height;
        _edge = edge;
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(OnMessage);
        var data = Data();
        data.Callback = CallbackMessage;
        _registered = SHAppBarMessage(0, ref data) != UIntPtr.Zero;
        Reposition();
    }

    public void SetEdge(string edge)
    {
        _edge = edge;
        Reposition();
    }

    private AppBarData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<AppBarData>(),
        Handle = _handle,
        Edge = _edge == "bottom" ? 3u : 1u
    };

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_disposed && message == CallbackMessage && wParam.ToInt32() == 1 && !_positioning)
            _window.Dispatcher.BeginInvoke(Reposition);
        return IntPtr.Zero;
    }

    public void Reposition()
    {
        if (_disposed || _positioning) return;
        _positioning = true;
        try
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(_handle, 1), ref info)) return;
            var dpi = GetDpiForWindow(_handle);
            var scale = (dpi == 0 ? 96 : dpi) / 96d;
            var data = Data();
            data.Rect = info.Monitor;
            var pixels = (int)Math.Ceiling(_height * scale);
            if (data.Edge == 1) data.Rect.Bottom = data.Rect.Top + pixels;
            else data.Rect.Top = data.Rect.Bottom - pixels;
            if (_registered)
            {
                SHAppBarMessage(2, ref data);
                if (data.Edge == 1) data.Rect.Bottom = data.Rect.Top + pixels;
                else data.Rect.Top = data.Rect.Bottom - pixels;
                SHAppBarMessage(3, ref data);
            }
            _window.Left = data.Rect.Left / scale;
            _window.Top = data.Rect.Top / scale;
            _window.Width = (data.Rect.Right - data.Rect.Left) / scale;
            _window.Height = _height;
        }
        finally { _positioning = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registered) { var data = Data(); SHAppBarMessage(1, ref data); }
        _source?.RemoveHook(OnMessage);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData { public uint Size; public IntPtr Handle; public uint Callback, Edge; public NativeRect Rect; public IntPtr Parameter; }
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
}
