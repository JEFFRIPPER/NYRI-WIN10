using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nyri.Win10.Services;

public sealed class ClipboardListener : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly HwndSource? _source;
    private readonly IntPtr _hwnd;
    private readonly Action<string> _onClipboard;

    public ClipboardListener(Window window, Action<string> onClipboard)
    {
        _onClipboard = onClipboard;
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        AddClipboardFormatListener(_hwnd);
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {        if (msg != WmClipboardUpdate)
            return IntPtr.Zero;

        Application.Current.Dispatcher.BeginInvoke(() =>
        {
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
    {        if (_hwnd != IntPtr.Zero)
            RemoveClipboardFormatListener(_hwnd);
        _source?.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}