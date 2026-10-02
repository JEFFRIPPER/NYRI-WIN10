using System.Runtime.InteropServices;

namespace Nyri.Win10.Services;

public sealed record ClipboardCapturePolicy(bool AllowMonitoring, bool AllowHistory);

public static class ClipboardPolicy
{
    private const string ExcludeFormatName = "ExcludeClipboardContentFromMonitorProcessing";
    private const string HistoryFormatName = "CanIncludeInClipboardHistory";
    private static readonly ClipboardCapturePolicy Denied = new(false, false);
    // Defer native registration: pure policy checks never touch the clipboard APIs.
    private static readonly Lazy<(uint Exclude, uint History)> Formats = new(() =>
        (RegisterClipboardFormat(ExcludeFormatName), RegisterClipboardFormat(HistoryFormatName)));

    // Windows distinguishes monitoring exclusion from a history-only DWORD marker.
    // The cloud marker does not govern local capture or local history.
    // https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats
    public static ClipboardCapturePolicy FromMarkers(bool excludePresent, bool historyMarkerPresent,
        uint? historyValue)
    {
        if (excludePresent)
            return Denied;
        return new ClipboardCapturePolicy(true, !historyMarkerPresent || historyValue == 1);
    }

    public static bool TryRead(IntPtr owner, out ClipboardCapturePolicy policy, out uint sequence)
    {
        policy = Denied;
        sequence = 0;
        if (!OpenClipboard(owner))
            return false;
        try
        {
            var formats = Formats.Value;
            if (formats.Exclude == 0 || formats.History == 0)
                return false;
            var excludePresent = IsClipboardFormatAvailable(formats.Exclude);
            // Exclusion is determined by presence, regardless of its payload.
            var historyPresent = !excludePresent && IsClipboardFormatAvailable(formats.History);
            var historyValue = historyPresent ? ReadHistoryDword(formats.History) : null;
            var capturedSequence = GetClipboardSequenceNumber();
            if (capturedSequence == 0)
                return false; // Windows also returns zero when clipboard access is denied.
            policy = FromMarkers(excludePresent, historyPresent, historyValue);
            sequence = capturedSequence;
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // Detect a clipboard replacement between this gate and subsequent text capture.
    // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber
    public static bool IsCurrentSequence(uint sequence) =>
        sequence != 0 && GetClipboardSequenceNumber() == sequence;

    private static uint? ReadHistoryDword(uint format)
    {
        // Avoid WPF GetData for custom markers: its .NET 8 OLE adapter may
        // deserialize a serialized-object prefix instead of returning raw bytes.
        // https://github.com/dotnet/wpf/blob/v8.0.0/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/dataobject.cs#L2873
        // Registered clipboard formats use HGLOBAL storage. These handles are
        // borrowed: copy only the DWORD while open, unlock, and never free them.
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerclipboardformatw
        // https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboarddata
        // GlobalSize may exceed the allocation requested by the source application.
        // https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-globalsize
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero || GlobalSize(handle).ToUInt64() < sizeof(uint))
            return null;
        var address = GlobalLock(handle);
        if (address == IntPtr.Zero)
            return null;
        try
        {
            return unchecked((uint)Marshal.ReadInt32(address));
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);
}
