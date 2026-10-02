using System.Windows.Media;

namespace Nyri.Win10.Models;

public sealed record TrackedWindow(
    IntPtr Handle,
    string Title,
    string ProcessName,
    ImageSource? Icon = null);
