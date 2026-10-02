using System.Windows.Media;

namespace Nyri.Win10.Models;

public sealed record LaunchableApp(
    string Id,
    string Name,
    string LaunchPath,
    ImageSource? Icon = null,
    string Glyph = "\uE8A5",
    string? Description = null);
