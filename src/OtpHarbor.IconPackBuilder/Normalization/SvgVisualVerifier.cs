using System.Globalization;
using System.Text;
using System.Xml.Linq;
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

internal sealed record RuntimeVisualCapture(
    SvgVisualMetrics SourceIdentity,
    byte[] SourceRgba);

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
    private const int CompositionProbeSize = 64;

    public static SvgVisualMetrics Compare(byte[] source, byte[] normalized, string backgroundColor)
    {
        using var sourceBitmap = RenderTile(source, backgroundColor);
        using var normalizedBitmap = RenderTile(normalized, backgroundColor);
        return CompareBitmaps(sourceBitmap, normalizedBitmap);
    }

    public static byte[] RenderRuntimeRgba(byte[] sourceSvg, int contentInset)
    {
        using var bitmap = RenderContent(sourceSvg, RuntimeIconRenderer.Width,
            SKColors.Transparent, contentInset);
        var rgba = new byte[RuntimeIconRenderer.MaximumDecodedBytes];
        var index = 0;
        foreach (var pixel in bitmap.Pixels)
        {
            rgba[index++] = pixel.Red;
            rgba[index++] = pixel.Green;
            rgba[index++] = pixel.Blue;
            rgba[index++] = pixel.Alpha;
        }
        return rgba;
    }

    public static RuntimeVisualCapture CaptureRuntimeRgba(
        byte[] sourceSvg,
        string backgroundColor,
        int contentInset)
    {
        using var source = RenderContent(sourceSvg, RuntimeIconRenderer.Width,
            SKColors.Transparent, contentInset);
        if (!HasVisiblePixel(source))
            throw new InputValidationException("Runtime visual capture produced blank source artwork.");
        using var sourceTile = CompositeRuntimeTile(source, backgroundColor);
        return new RuntimeVisualCapture(
            CompareBitmaps(sourceTile, sourceTile),
            Rgba(source));
    }

    public static SvgVisualMetrics CompareCanonicalToRuntimePng(
        byte[] canonicalSvg,
        byte[] runtimePng,
        string backgroundColor,
        int contentInset = 0)
    {
        var profiledSvg = RuntimeIconRenderer.ApplyRuntimePaintProfile(canonicalSvg);
        using var canonical = RenderContent(profiledSvg, RuntimeIconRenderer.Width,
            SKColors.Transparent, contentInset);
        using var runtime = DeterministicPng.DecodeAndValidate(runtimePng,
            RuntimeIconRenderer.Width, RuntimeIconRenderer.Height, "runtime visual verification");
        if (!HasVisiblePixel(canonical) || !HasVisiblePixel(runtime))
            return new SvgVisualMetrics(double.MaxValue, TileSize * TileSize, TileSize * TileSize,
                [], [], false);
        var alphaMismatch = !AlphaPixelsMatch(canonical, runtime);
        using var canonicalTile = CompositeRuntimeTile(canonical, backgroundColor);
        using var runtimeTile = CompositeRuntimeTile(runtime, backgroundColor);
        var metrics = CompareBitmaps(canonicalTile, runtimeTile);
        return alphaMismatch ? metrics with { Resolved = false } : metrics;
    }

    public static SvgVisualMetrics CompareSourceToRuntimePng(
        byte[] sourceSvg,
        byte[] runtimePng,
        string backgroundColor,
        int contentInset)
    {
        using var source = RenderContent(sourceSvg, RuntimeIconRenderer.Width,
            SKColors.Transparent, contentInset);
        using var runtime = DeterministicPng.DecodeAndValidate(runtimePng,
            RuntimeIconRenderer.Width, RuntimeIconRenderer.Height, "source-to-runtime visual verification");
        if (!HasVisiblePixel(source) || !HasVisiblePixel(runtime))
            return new SvgVisualMetrics(double.MaxValue, TileSize * TileSize, TileSize * TileSize,
                [], [], false);
        using var sourceTile = CompositeRuntimeTile(source, backgroundColor);
        using var runtimeTile = CompositeRuntimeTile(runtime, backgroundColor);
        return CompareBitmaps(sourceTile, runtimeTile);
    }

    public static SvgVisualMetrics CompareExpectedRgbaToRuntimePng(
        byte[] expectedRgba,
        byte[] runtimePng,
        string backgroundColor)
    {
        using var runtime = DeterministicPng.DecodeAndValidate(runtimePng,
            RuntimeIconRenderer.Width, RuntimeIconRenderer.Height, "runtime pixel verification");
        return CompareExpectedRgbaToRuntimeBitmap(expectedRgba, runtime, backgroundColor);
    }

    public static SvgVisualMetrics CompareExpectedRgbaToRuntimeBitmap(
        byte[] expectedRgba,
        SKBitmap runtime,
        string backgroundColor)
    {
        if (expectedRgba.Length != RuntimeIconRenderer.MaximumDecodedBytes)
            throw new ArgumentException("Expected runtime RGBA has an invalid byte length.", nameof(expectedRgba));
        var offset = 0;
        var exact = true;
        foreach (var pixel in runtime.Pixels)
        {
            exact &= pixel.Red == expectedRgba[offset]
                && pixel.Green == expectedRgba[offset + 1]
                && pixel.Blue == expectedRgba[offset + 2]
                && pixel.Alpha == expectedRgba[offset + 3];
            offset += 4;
        }
        if (!exact || !HasVisiblePixel(runtime))
            return new SvgVisualMetrics(double.MaxValue, TileSize * TileSize, TileSize * TileSize,
                [], [], false);
        using var runtimeTile = CompositeRuntimeTile(runtime, backgroundColor);
        return CompareBitmaps(runtimeTile, runtimeTile);
    }

    public static SvgVisualMetrics CompareCanonicalToReviewedBadgeRuntimePng(
        byte[] canonicalSvg,
        byte[] runtimePng,
        string matteColor,
        string previewBackgroundColor)
    {
        using var expected = RuntimeIconRenderer.CreateReviewedBadgeBitmap(
            canonicalSvg, matteColor, "reviewed badge visual verification");
        using var runtime = DeterministicPng.DecodeAndValidate(runtimePng,
            RuntimeIconRenderer.Width, RuntimeIconRenderer.Height, "reviewed badge runtime visual verification");
        if (!HasVisiblePixel(expected) || !HasVisiblePixel(runtime))
            return new SvgVisualMetrics(double.MaxValue, TileSize * TileSize, TileSize * TileSize,
                [], [], false);
        var alphaMismatch = !AlphaPixelsMatch(expected, runtime);
        using var expectedTile = CompositeRuntimeTile(expected, previewBackgroundColor);
        using var runtimeTile = CompositeRuntimeTile(runtime, previewBackgroundColor);
        var metrics = CompareBitmaps(expectedTile, runtimeTile);
        return alphaMismatch ? metrics with { Resolved = false } : metrics;
    }

    private static SvgVisualMetrics CompareBitmaps(SKBitmap sourceBitmap, SKBitmap normalizedBitmap)
    {
        long totalDifference = 0;
        var materiallyDifferent = 0;
        if (sourceBitmap.Width != normalizedBitmap.Width || sourceBitmap.Height != normalizedBitmap.Height)
            throw new ArgumentException("Visual comparison bitmaps must have equal dimensions.");
        for (var y = 0; y < sourceBitmap.Height; y++)
            for (var x = 0; x < sourceBitmap.Width; x++)
            {
                var sourcePixel = sourceBitmap.GetPixel(x, y);
                var normalizedPixel = normalizedBitmap.GetPixel(x, y);
                var difference = Math.Abs(sourcePixel.Red - normalizedPixel.Red)
                    + Math.Abs(sourcePixel.Green - normalizedPixel.Green)
                    + Math.Abs(sourcePixel.Blue - normalizedPixel.Blue);
                totalDifference += difference;
                if (difference > MaterialPixelRgbDifference) materiallyDifferent++;
            }
        var totalPixels = checked(sourceBitmap.Width * sourceBitmap.Height);
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

    public static bool RecognizesEmbeddedComposition(byte[] canonicalSvg)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(Encoding.UTF8.GetString(canonicalSvg), LoadOptions.None);
        }
        catch
        {
            return false;
        }
        var root = document.Root;
        if (root is null) return false;
        var paths = root.Elements().Where(element => element.Name.LocalName == "path").ToArray();
        if (paths.Length < 2) return false;
        var viewBox = root.Attribute("viewBox")?.Value;
        if (string.IsNullOrWhiteSpace(viewBox)) return false;

        using var complete = RenderContent(canonicalSvg, CompositionProbeSize, SKColors.Transparent);
        var bounds = VisibleBounds(complete);
        if (bounds is null) return false;
        var boundsArea = checked((bounds.Value.Right - bounds.Value.Left + 1)
            * (bounds.Value.Bottom - bounds.Value.Top + 1));

        for (var candidateIndex = 0; candidateIndex < paths.Length - 1; candidateIndex++)
        {
            var candidateFill = SolidFill(paths[candidateIndex]);
            if (candidateFill is null) continue;
            var details = paths[(candidateIndex + 1)..]
                .Where(path => SolidFill(path) is { } fill
                    && !fill.Equals(candidateFill, StringComparison.Ordinal))
                .ToArray();
            if (details.Length == 0) continue;

            using var candidate = RenderCanonicalSubset(root, [paths[candidateIndex]]);
            var candidatePixels = candidate.Pixels.Count(pixel => pixel.Alpha >= 240);
            if (candidatePixels < boundsArea * 0.50) continue;

            using var detail = RenderCanonicalSubset(root, details);
            var detailPixels = 0;
            var containedDetailPixels = 0;
            for (var y = 0; y < CompositionProbeSize; y++)
                for (var x = 0; x < CompositionProbeSize; x++)
                {
                    if (detail.GetPixel(x, y).Alpha < 64) continue;
                    detailPixels++;
                    if (candidate.GetPixel(x, y).Alpha >= 64) containedDetailPixels++;
                }
            if (detailPixels >= 16 && containedDetailPixels >= detailPixels * 0.75)
                return true;
        }
        return false;
    }

    public static byte[] RasterFlatten(
        byte[] source,
        string backgroundColor,
        int rasterSize)
    {
        if (rasterSize < ContentSize || rasterSize > RuntimeIconRenderer.Width)
            throw new ArgumentOutOfRangeException(nameof(rasterSize));
        var background = ParseColor(backgroundColor);
        var backgroundRgb = ((uint)background.Red << 16) | ((uint)background.Green << 8) | background.Blue;
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
                // A subpixel can round exactly to the tile color after alpha
                // compositing. Keeping it as an opaque path would preserve no
                // visible artwork and would create a foreground/background
                // paint collision in the canonical fallback.
                if (color == backgroundRgb) { x++; continue; }
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

    private static SKBitmap RenderCanonicalSubset(XElement root, IReadOnlyList<XElement> paths)
    {
        var subset = new XElement(root.Name,
            root.Attributes().Where(attribute => attribute.Name.LocalName == "viewBox"),
            paths.Select(path => new XElement(path)));
        var bytes = Encoding.UTF8.GetBytes(new XDocument(subset).ToString(SaveOptions.DisableFormatting));
        return RenderContent(bytes, CompositionProbeSize, SKColors.Transparent);
    }

    private static string? SolidFill(XElement path)
    {
        var fill = path.Attribute("fill")?.Value.ToUpperInvariant();
        return fill is { Length: 7 } && fill[0] == '#' ? fill : null;
    }

    private static (int Left, int Top, int Right, int Bottom)? VisibleBounds(SKBitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha >= 64)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
        return right < left ? null : (left, top, right, bottom);
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

    private static SKBitmap RenderContent(byte[] svg, int size, SKColor background, int contentInset = 0)
    {
        if (contentInset < 0 || contentInset * 2 >= size)
            throw new ArgumentOutOfRangeException(nameof(contentInset));
        var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background);
        Draw(svg, canvas, new SKRect(contentInset, contentInset,
            size - contentInset, size - contentInset));
        canvas.Flush();
        return bitmap;
    }

    private static SKBitmap CompositeRuntimeTile(SKBitmap foreground, string backgroundColor)
    {
        var tile = new SKBitmap(TileSize, TileSize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(tile);
        canvas.Clear(ParseColor(backgroundColor));
        canvas.DrawBitmap(foreground,
            new SKRect(0, 0, foreground.Width, foreground.Height),
            new SKRect(8, 8, 8 + ContentSize, 8 + ContentSize),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        canvas.Flush();
        return tile;
    }

    private static bool HasVisiblePixel(SKBitmap bitmap)
    {
        foreach (var pixel in bitmap.Pixels)
            if (pixel.Alpha != 0) return true;
        return false;
    }

    private static byte[] Rgba(SKBitmap bitmap)
    {
        var rgba = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        var offset = 0;
        foreach (var pixel in bitmap.Pixels)
        {
            rgba[offset++] = pixel.Red;
            rgba[offset++] = pixel.Green;
            rgba[offset++] = pixel.Blue;
            rgba[offset++] = pixel.Alpha;
        }
        return rgba;
    }

    private static bool AlphaPixelsMatch(SKBitmap left, SKBitmap right)
    {
        if (left.Width != right.Width || left.Height != right.Height) return false;
        for (var y = 0; y < left.Height; y++)
            for (var x = 0; x < left.Width; x++)
                if (left.GetPixel(x, y).Alpha != right.GetPixel(x, y).Alpha)
                    return false;
        return true;
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
        // allotted icon tile. Compatible importers clip icons to this same square.
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
