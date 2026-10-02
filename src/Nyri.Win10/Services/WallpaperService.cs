using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Nyri.Win10.Services;

/// <summary>Native vector previews and explicit application through Windows wallpaper API.</summary>
public sealed class WallpaperService
{
    private static readonly Lazy<XDocument> Bauhaus = new(() =>
    {
        using var source = typeof(WallpaperService).Assembly.GetManifestResourceStream("Nyri.Bauhaus.svg")!;
        return XDocument.Load(source);
    });
    private static readonly string[] OriginalColors = ["#6b3f24", "#d8603b", "#e97a2f", "#f3c6a0", "#f7d7b8"];

    public BitmapSource Preview(string style, string palette, int width = 640, int height = 360)
    {
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384) throw new ArgumentOutOfRangeException(nameof(width));
        var colors = Palette(palette);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (style == "waves") DrawWaves(drawing, colors, width, height);
            else
            {
                var scale = Math.Max(width / 2880d, height / 1800d);
                drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
                drawing.PushTransform(new TranslateTransform((width - 2880 * scale) / 2, (height - 1800 * scale) / 2));
                drawing.PushTransform(new ScaleTransform(scale, scale));
                DrawSvg(drawing, Bauhaus.Value.Root!, colors);
                drawing.Pop(); drawing.Pop(); drawing.Pop();
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public string Apply(string style, string palette)
    {
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        var preview = Preview(style, palette, width, height);
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NYRI-WIN10", "wallpapers");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"nyri-{(style == "waves" ? "waves" : "bauhaus")}-{(palette is "purple" or "ocean" ? palette : "terracotta")}.bmp");
        using (var file = File.Create(path))
        {
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(preview));
            encoder.Save(file);
        }
        if (!SystemParametersInfo(0x0014, 0, path, 0x0001 | 0x0002))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not apply the wallpaper.");
        return path;
    }

    private static string[] Palette(string name) => name switch
    {
        "purple" => ["#332C50", "#583B92", "#8760C5", "#B29ACF", "#DED0E9"],
        "ocean" => ["#153F47", "#277481", "#4BA8AE", "#A3D4CA", "#D8EEE6"],
        _ => OriginalColors
    };
    private static Brush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private static double Number(XElement element, string name) => double.Parse((string?)element.Attribute(name) ?? "0", CultureInfo.InvariantCulture);

    // The bundled, MIT-licensed upstream SVG uses only these primitives/transforms.
    // This deliberately handles that asset, rather than promising general SVG support.
    private static void DrawSvg(DrawingContext drawing, XElement element, string[] colors)
    {
        var transform = (string?)element.Attribute("transform");
        if (transform is not null) drawing.PushTransform(new MatrixTransform(ParseTransform(transform)));
        var fill = (string?)element.Attribute("fill");
        var index = Array.IndexOf(OriginalColors, fill?.ToLowerInvariant());
        var ink = fill is null ? null : Brush(index >= 0 ? colors[index] : fill);
        switch (element.Name.LocalName)
        {
            case "rect": drawing.DrawRectangle(ink, null, new Rect(Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height"))); break;
            case "circle": drawing.DrawEllipse(ink, null, new Point(Number(element, "cx"), Number(element, "cy")), Number(element, "r"), Number(element, "r")); break;
            case "path": drawing.DrawGeometry(ink, null, Geometry.Parse((string)element.Attribute("d")!)); break;
        }
        foreach (var child in element.Elements()) DrawSvg(drawing, child, colors);
        if (transform is not null) drawing.Pop();
    }

    private static Matrix ParseTransform(string text)
    {
        var result = Matrix.Identity;
        foreach (Match match in Regex.Matches(text, @"(translate|scale|rotate)\(([^)]+)\)"))
        {
            var values = match.Groups[2].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            var next = Matrix.Identity;
            switch (match.Groups[1].Value)
            {
                case "translate": next.Translate(values[0], values.Length > 1 ? values[1] : 0); break;
                case "scale": next.Scale(values[0], values.Length > 1 ? values[1] : values[0]); break;
                case "rotate": next.RotateAt(values[0], values.Length > 2 ? values[1] : 0, values.Length > 2 ? values[2] : 0); break;
            }
            result.Prepend(next);
        }
        return result;
    }

    private static void DrawWaves(DrawingContext drawing, string[] colors, int width, int height)
    {
        drawing.DrawRectangle(Brush(colors[0]), null, new Rect(0, 0, width, height));
        for (var band = 1; band <= 6; band++)
        {
            var shape = new StreamGeometry();
            using (var path = shape.Open())
            {
                path.BeginFigure(new Point(-1, height + 1), true, true);
                for (var step = 0; step <= 180; step++)
                {
                    var x = width * step / 180d;
                    var y = height * band / 7d + height * .05 * Math.Sin(step / 180d * Math.PI * 2 + band * .8) + height * .025 * Math.Sin(step / 180d * Math.PI * 4 - band * .3);
                    path.LineTo(new Point(x, y), true, false);
                }
                path.LineTo(new Point(width + 1, height + 1), true, false);
            }
            drawing.DrawGeometry(Brush(colors[band < 5 ? band : 8 - band]), null, shape);
        }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
