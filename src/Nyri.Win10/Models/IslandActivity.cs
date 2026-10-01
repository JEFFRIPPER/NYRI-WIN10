namespace Nyri.Win10.Models;

public enum IslandActivityKind
{
    System,
    Network,
    Clipboard,
    Media,
    Microphone,
    Camera,
    Timer,
    Vpn,
    Transfer
}

public sealed record IslandActivity(
    string Id,
    IslandActivityKind Kind,
    string Title,
    string Detail,
    string Glyph,
    DateTimeOffset UpdatedAt,
    bool IsActive = true,
    int Priority = 0,
    bool Ambient = false
);