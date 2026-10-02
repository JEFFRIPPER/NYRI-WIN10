using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nyri.Win10.Services;

internal static class WindowIconReader
{
    public static ImageSource? Read(IntPtr hwnd, CancellationToken token)
    {
        // All calls run on the tracker worker. Never wait for another application's
        // window procedure from the WPF dispatcher, and stop querying hung windows.
        var icon = ReadMessage(hwnd, 1, out var responsive); // ICON_BIG
        if (icon == IntPtr.Zero)
            icon = ReadClassIcon(hwnd, -14); // GCLP_HICON
        if (icon == IntPtr.Zero && responsive && !token.IsCancellationRequested)
            icon = ReadMessage(hwnd, 0, out responsive); // ICON_SMALL
        if (icon == IntPtr.Zero)
            icon = ReadClassIcon(hwnd, -34); // GCLP_HICONSM
        if (icon == IntPtr.Zero && responsive && !token.IsCancellationRequested)
            icon = ReadMessage(hwnd, 2, out _); // ICON_SMALL2
        if (icon == IntPtr.Zero || token.IsCancellationRequested)
            return null;

        // These are borrowed handles belonging to the source window/class. Copy it
        // before conversion and release only our own copy.
        var ownedIcon = CopyIcon(icon);
        if (ownedIcon == IntPtr.Zero)
            return null;
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(ownedIcon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or COMException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(ownedIcon);
        }
    }

    private static IntPtr ReadMessage(IntPtr hwnd, uint iconType, out bool responsive)
    {
        const uint wmGetIcon = 0x007F;
        const uint smtoBlockAbortIfHungErrorOnExit = 0x0001 | 0x0002 | 0x0020;
        responsive = SendMessageTimeout(hwnd, wmGetIcon, new UIntPtr(iconType), IntPtr.Zero,
            smtoBlockAbortIfHungErrorOnExit, 50, out var result) != IntPtr.Zero;
        if (!responsive)
            return IntPtr.Zero;
        return IntPtr.Size == 8
            ? new IntPtr(unchecked((long)result.ToUInt64()))
            : new IntPtr(unchecked((int)result.ToUInt32()));
    }

    private static IntPtr ReadClassIcon(IntPtr hwnd, int index) => IntPtr.Size == 8
        ? GetClassLongPtr(hwnd, index)
        : new IntPtr(unchecked((int)GetClassLong(hwnd, index)));

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern uint GetClassLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr CopyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
