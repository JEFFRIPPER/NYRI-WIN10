using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Nyri.Win10.Services;

public sealed class ClipboardListener : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly HwndSource? _source;
    private readonly IntPtr _hwnd;
    private readonly Action<string> _onClipboard;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public ClipboardListener(Window window, Action<string> onClipboard)
    {
        _dispatcher = window.Dispatcher;
        _onClipboard = onClipboard;
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        if (_source is null) throw new InvalidOperationException("Clipboard listener requires a live window handle.");
        _source.AddHook(WndProc);
        if (!AddClipboardFormatListener(_hwnd))
        {
            var error = Marshal.GetLastWin32Error();
            _source.RemoveHook(WndProc);
            throw new Win32Exception(error, "Windows could not attach clipboard notifications.");
        }
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {        if (_disposed || _dispatcher.HasShutdownStarted || msg != WmClipboardUpdate)
            return IntPtr.Zero;

        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            try
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText().ReplaceLineEndings(" ");
                    _onClipboard(text.Length > 72 ? text[..72] + "…" : text);
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    _onClipboard($"Файлов в буфере: {Clipboard.GetFileDropList().Count}");
                }
            }
            catch
            {
                // Clipboard may be temporarily locked by another process.
            }
        });

        return IntPtr.Zero;
    }

    public void Dispose()
    {        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero)
            RemoveClipboardFormatListener(_hwnd);
        _source?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
