using System.Globalization;
using System.Text;
using SkiaSharp;
using Svg.Skia;

namespace OtpHarbor.IconPackBuilder.Normalization;

public sealed record SvgVisualMetrics(
    double MeanAbsoluteChannelDifference,
    int MateriallyDifferentPixels,
    int TotalPixels,
    IReadOnlyList<string> SourcePalette,
    IReadOnlyList<string> NormalizedPalette,
    bool Resolved);

internal static class SvgVisualVerifier
{
    private const int TileSize = 40;
    private const int ContentSize = 24;
    // A path-rasterized fallback is sampled once and then rendered again, so edge
    // antialiasing cannot be byte-identical. These bounds allow that small edge
    // noise while still rejecting missing geometry or materially wrong paint.
    private const double MaximumMeanChannelDifference = 3.0;
    private const double MaximumMaterialPixelRatio = 0.03;
    private const int MaterialPixelRgbDifference = 96;

    public static SvgVisualMetrics Compare(byte[] source, byte[] normalized, string backgroundColor)
    {
        using var sourceBitmap = RenderTile(source, backgroundColor);
        using var normalizedBitmap = RenderTile(normalized, backgroundColor);
        long totalDifference = 0;
        var materiallyDifferent = 0;
        for (var y = 0; y < TileSize; y++)
            for (var x = 0; x < TileSize; x++)
            {
                var sourcePixel = sourceBitmap.GetPixel(x, y);
                var normalizedPixel = normalizedBitmap.GetPixel(x, y);
                var difference = Math.Abs(sourcePixel.Red - normalizedPixel.Red)
                    + Math.Abs(sourcePixel.Green - normalizedPixel.Green)
                    + Math.Abs(sourcePixel.Blue - normalizedPixel.Blue);
                totalDifference += difference;
                if (difference > MaterialPixelRgbDifference) materiallyDifferent++;
            }
        var totalPixels = TileSize * TileSize;
        var mean = totalDifference / (double)(totalPixels * 3);
        return new SvgVisualMetrics(
            Math.Round(mean, 4),
            materiallyDifferent,
            totalPixels,
            Palette(sourceBitmap),
            Palette(normalizedBitmap),
            mean <= MaximumMeanChannelDifference
                && materiallyDifferent <= totalPixels * MaximumMaterialPixelRatio);
    }

    public static byte[] RasterFlatten(byte[] source, string backgroundColor, int rasterSize)
    {
        if (rasterSize < ContentSize || rasterSize % ContentSize != 0)
            throw new ArgumentOutOfRangeException(nameof(rasterSize));
        var background = ParseColor(backgroundColor);
        using var bitmap = RenderContent(source, rasterSize, SKColors.Transparent);
        var runs = new List<RasterRun>();
        var coordinateScale = ContentSize / (double)rasterSize;
        var overlap = rasterSize == ContentSize ? 0 : coordinateScale / 2;
        for (var y = 0; y < rasterSize; y++)
        {
            var x = 0;
            while (x < rasterSize)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha == 0) { x++; continue; }
                var color = Composite(pixel, background);
                var start = x++;
                while (x < rasterSize && Composite(bitmap.GetPixel(x, y), background) == color
                       && bitmap.GetPixel(x, y).Alpha > 0) x++;
                var left = start * coordinateScale;
                var top = y * coordinateScale;
                var right = Math.Min(ContentSize, x * coordinateScale + overlap);
                var bottom = Math.Min(ContentSize, (y + 1) * coordinateScale + overlap);
                runs.Add(new RasterRun(color, left, top, right, bottom));
            }
        }
        if (runs.Count == 0) throw new InputValidationException("Reference rendering produced a blank icon.");
        var output = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\">");
        foreach (var run in runs)
            output.Append("<path fill=\"").Append(ColorText(run.Color)).Append("\" d=\"M")
                .Append(Format(run.Left)).Append(' ').Append(Format(run.Top))
                .Append('H').Append(Format(run.Right)).Append('V').Append(Format(run.Bottom))
                .Append('H').Append(Format(run.Left)).Append("Z\"/>");
        output.Append("</svg>");
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    private static SKBitmap RenderTile(byte[] svg, string backgroundColor)
    {
        var bitmap = new SKBitmap(TileSize, TileSize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ParseColor(backgroundColor));
        Draw(svg, canvas, new SKRect(8, 8, 8 + ContentSize, 8 + ContentSize));
        canvas.Flush();
        return bitmap;
    }

    private static SKBitmap RenderContent(byte[] svg, int size, SKColor background)
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background);
        Draw(svg, canvas, new SKRect(0, 0, size, size));
        canvas.Flush();
        return bitmap;
    }

    private static void Draw(byte[] data, SKCanvas canvas, SKRect target)
    {
        using var svg = new SKSvg();
        using var stream = new MemoryStream(data, writable: false);
        var picture = svg.Load(stream) ?? throw new InputValidationException("SVG reference renderer could not load the asset.");
        var bounds = picture.CullRect;
        if (!float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
            throw new InputValidationException("SVG reference renderer produced invalid bounds.");
        var scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
        var x = target.Left + (target.Width - bounds.Width * scale) / 2;
        var y = target.Top + (target.Height - bounds.Height * scale) / 2;
        canvas.Save();
        // Compare the icon viewport, not filter/shadow output that overflows its
        // allotted icon tile. OTP Harbor clips icons to this same square.
        canvas.ClipRect(target);
        canvas.Translate(x, y);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static IReadOnlyList<string> Palette(SKBitmap bitmap) => bitmap.Pixels
        .GroupBy(pixel => ((uint)pixel.Red << 16) | ((uint)pixel.Green << 8) | pixel.Blue)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key)
        .Take(32)
        .Select(group => ColorText(group.Key) + ":" + group.Count().ToString(CultureInfo.InvariantCulture))
        .ToArray();

    private static SKColor ParseColor(string value) => new(
        byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber),
        byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber),
        byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber));

    private static uint Composite(SKColor foreground, SKColor background)
    {
        var alpha = foreground.Alpha / 255d;
        var red = (byte)Math.Round(foreground.Red * alpha + background.Red * (1 - alpha));
        var green = (byte)Math.Round(foreground.Green * alpha + background.Green * (1 - alpha));
        var blue = (byte)Math.Round(foreground.Blue * alpha + background.Blue * (1 - alpha));
        return ((uint)red << 16) | ((uint)green << 8) | blue;
    }

    private static string ColorText(uint color) => $"#{(color >> 16) & 0xFF:X2}{(color >> 8) & 0xFF:X2}{color & 0xFF:X2}";
    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private readonly record struct RasterRun(uint Color, double Left, double Top, double Right, double Bottom);
}
