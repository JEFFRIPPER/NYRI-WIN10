using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nyri.Win10.Services;

/// <summary>
/// Applies explicit Material role presets to all shell surfaces. These palettes are curated,
/// not a wallpaper-derived Material color calculation.
/// </summary>
public sealed class ThemeService
{
    private readonly ResourceDictionary _resources;
    private readonly Dispatcher _dispatcher;
    private ResourceDictionary? _activePalette;

    public ThemeService(ResourceDictionary resources)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Apply(true, "terracotta");
    }

    public bool Dark { get; private set; } = true;
    public string Palette { get; private set; } = "terracotta";
    public static IReadOnlyList<string> PaletteNames { get; } =
        Array.AsReadOnly(new[] { "terracotta", "purple", "ocean" });
    public event EventHandler? Changed;

    public void Apply(bool dark, string palette)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => Apply(dark, palette));
            return;
        }

        var name = palette?.Trim().ToLowerInvariant() ?? "terracotta";
        if (!PaletteNames.Contains(name)) name = "terracotta";
        if (_activePalette is not null && Dark == dark && Palette == name) return;

        // Build and freeze every brush first. A single merged-dictionary replacement keeps
        // consumers from observing a half-applied palette during resource invalidation.
        var next = CreatePalette(dark, name);
        AddMotionResources(next);
        if (_activePalette is null)
        {
            _resources.MergedDictionaries.Add(next);
        }
        else
        {
            var index = _resources.MergedDictionaries.IndexOf(_activePalette);
            if (index >= 0) _resources.MergedDictionaries[index] = next;
            else _resources.MergedDictionaries.Add(next);
        }
        _activePalette = next;
        Dark = dark;
        Palette = name;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Respects Windows' animation preference for callers constructing animations.</summary>
    public static Duration MotionDuration(int milliseconds) =>
        new(SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.Zero);

    private static void AddMotionResources(ResourceDictionary resources)
    {
        resources["MotionFastSpatial"] = MotionDuration(367);
        resources["MotionDefaultSpatial"] = MotionDuration(466);
        resources["MotionSlowSpatial"] = MotionDuration(504);
        resources["MotionEffects"] = MotionDuration(231);
        resources["MotionEnabled"] = SystemParameters.ClientAreaAnimation;
    }

    private static ResourceDictionary CreatePalette(bool dark, string name)
    {
        var resources = new ResourceDictionary();
        var roles = new Dictionary<string, string>(StringComparer.Ordinal);
        SetRoles(roles, dark
            ? ["#1A110C", "#F0DFD7", "#DBC1B3", "#140C08", "#231914", "#281D18", "#332822", "#3E322C", "#423630", "#A48C7F", "#564338", "#F2DFD5", "#392E28"]
            : ["#FFF8F5", "#231914", "#53443B", "#FFFFFF", "#FFF1E9", "#FBEAE1", "#F5E4DB", "#EFDED5", "#FFF8F5", "#857369", "#D8C2B5", "#392E28", "#FFF1E9"]);

        switch (name)
        {
            case "purple":
                SetAccent(roles, dark
                    ? ["#D0BCFF", "#381E72", "#4F378B", "#EADDFF", "#CCC2DC", "#332D41", "#4A4458", "#E8DEF8", "#EFB8C8", "#492532", "#633B48", "#FFD8E4"]
                    : ["#6750A4", "#FFFFFF", "#EADDFF", "#21005D", "#625B71", "#FFFFFF", "#E8DEF8", "#1D192B", "#7D5260", "#FFFFFF", "#FFD8E4", "#31111D"]);
                SetRoles(roles, dark
                    ? ["#141218", "#E6E0E9", "#CAC4D0", "#0F0D13", "#1D1B20", "#211F26", "#2B2930", "#36343B", "#3B383E", "#938F99", "#49454F", "#E6E0E9", "#322F35"]
                    : ["#FEF7FF", "#1D1B20", "#49454F", "#FFFFFF", "#F7F2FA", "#F3EDF7", "#ECE6F0", "#E6E0E9", "#FEF7FF", "#79747E", "#CAC4D0", "#322F35", "#F5EFF7"]);
                break;
            case "ocean":
                SetAccent(roles, dark
                    ? ["#89D1E5", "#003640", "#004E5C", "#ACEBFF", "#B3CAD0", "#1E3339", "#354A50", "#CFE6EC", "#BCC5EB", "#252F4C", "#3C4664", "#DCE1FF"]
                    : ["#006879", "#FFFFFF", "#ACEBFF", "#001F26", "#4C6268", "#FFFFFF", "#CFE6EC", "#071F25", "#545E7D", "#FFFFFF", "#DCE1FF", "#111B36"]);
                SetRoles(roles, dark
                    ? ["#0F1416", "#DEE3E6", "#BFC8CC", "#0A0F11", "#171C1E", "#1B2022", "#252B2D", "#303638", "#363C3E", "#899296", "#3F484C", "#DEE3E6", "#2C3133"]
                    : ["#F5FAFC", "#171C1E", "#3F484C", "#FFFFFF", "#EFF4F6", "#E9EFF1", "#E3E9EB", "#DEE3E6", "#F5FAFC", "#6F797D", "#BFC8CC", "#2C3133", "#EFF4F6"]);
                break;
            default:
                SetAccent(roles, dark
                    ? ["#FFB68C", "#532200", "#72350E", "#FFDBC9", "#E5BFAA", "#432B1D", "#5C4132", "#FFDBC9", "#C7CE44", "#2F3300", "#444900", "#E3EB61"]
                    : ["#984700", "#FFFFFF", "#FFDBC9", "#331200", "#765846", "#FFFFFF", "#FFDBC9", "#2B170B", "#626600", "#FFFFFF", "#E3EB61", "#1D2000"]);
                break;
        }

        var errorRoles = dark
            ? new[] { "#FFB4AB", "#690005", "#93000A", "#FFDAD6" }
            : new[] { "#BA1A1A", "#FFFFFF", "#FFDAD6", "#410002" };
        var errorKeys = new[] { "ErrorBrush", "OnErrorBrush", "ErrorContainerBrush", "OnErrorContainerBrush" };
        for (var i = 0; i < errorKeys.Length; i++) roles[errorKeys[i]] = errorRoles[i];
        roles["ShadowBrush"] = "#000000";
        roles["ScrimBrush"] = "#99000000";
        roles["MutedBrush"] = roles["OnSurfaceVariantBrush"];
        roles["SurfaceVariantBrush"] = roles["SurfaceContainerHighestBrush"];
        roles["ShellSurfaceBrush"] = roles["SurfaceContainerLowBrush"];
        roles["ShellPanelBrush"] = roles["SurfaceContainerBrush"];
        roles["ShellTileBrush"] = roles["SurfaceContainerHighBrush"];
        roles["ShellAccentBrush"] = roles["PrimaryBrush"];
        roles["ShellOnAccentBrush"] = roles["OnPrimaryBrush"];
        roles["ShellTextBrush"] = roles["OnSurfaceBrush"];
        roles["ShellMutedBrush"] = roles["OnSurfaceVariantBrush"];

        foreach (var (key, value) in roles)
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
        return resources;
    }

    private static void SetRoles(IDictionary<string, string> roles, string[] values)
    {
        string[] keys = ["SurfaceBrush", "OnSurfaceBrush", "OnSurfaceVariantBrush", "SurfaceContainerLowestBrush",
            "SurfaceContainerLowBrush", "SurfaceContainerBrush", "SurfaceContainerHighBrush", "SurfaceContainerHighestBrush",
            "SurfaceBrightBrush", "OutlineBrush", "OutlineVariantBrush", "InverseSurfaceBrush", "InverseOnSurfaceBrush"];
        for (var i = 0; i < keys.Length; i++) roles[keys[i]] = values[i];
    }

    private static void SetAccent(IDictionary<string, string> roles, string[] values)
    {
        string[] keys = ["PrimaryBrush", "OnPrimaryBrush", "PrimaryContainerBrush", "OnPrimaryContainerBrush",
            "SecondaryBrush", "OnSecondaryBrush", "SecondaryContainerBrush", "OnSecondaryContainerBrush",
            "TertiaryBrush", "OnTertiaryBrush", "TertiaryContainerBrush", "OnTertiaryContainerBrush"];
        for (var i = 0; i < keys.Length; i++) roles[keys[i]] = values[i];
    }
}
