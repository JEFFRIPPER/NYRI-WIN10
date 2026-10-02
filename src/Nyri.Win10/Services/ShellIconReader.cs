using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nyri.Win10.Services;

internal static class ShellIconReader
{
    public static ImageSource? Read(string path)
    {
        // SHGetFileInfo returns an owned HICON. WPF copies it before it is released.
        const uint shgfiIcon = 0x100;
        const uint shgfiSmallIcon = 0x1;
        var apartment = CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
        if (apartment < 0 && apartment != unchecked((int)0x80010106)) // RPC_E_CHANGED_MODE
            return null;
        ShellFileInfo info = default;
        try
        {
            if (SHGetFileInfo(path, 0, out info, (uint)Marshal.SizeOf<ShellFileInfo>(),
                    shgfiIcon | shgfiSmallIcon) == IntPtr.Zero || info.Icon == IntPtr.Zero)
                return null;

            var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty,
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
            if (info.Icon != IntPtr.Zero)
                DestroyIcon(info.Icon);
            if (apartment >= 0)
                CoUninitialize();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes,
        out ShellFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}
