using System.Text;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;
using Svg.Skia;

namespace OtpHarbor.IconPackBuilder.Normalization;

public sealed record RuntimeIconRenderResult(byte[] Png, string Sha256, SvgVisualMetrics VisualMetrics);

public static class RuntimeIconRenderer
{
    private static readonly string[] NeutralBackgrounds = ["#FFFFFF", "#000000", "#767676", "#334155"];
    public const string LightThemeSurface = "#FFFFFF";
    public const string DarkThemeSurface = "#10213F";
    public const double MinimumThemeSurfaceContrastRatio = 3.0;
    public const int Width = 128;
    public const int Height = 128;
    public const int MaximumDecodedBytes = Width * Height * 4;
    public const string MediaType = "image/png";
    public const string RenderProfile = "otp-harbor-foreground-v1";
    public const string ThemeRenderProfile = "otp-harbor-themed-appearance-v1";
    public const string EnclosedMatteComposition = "enclosed-transparent-matte";
    public const string SquareFrameComposition = "square-badge-matte-frame";
    public const int SquareFrameWidth = 4;
    public const int ThemeContentInset = 8;

    public static RuntimeIconRenderResult Render(
        byte[] canonicalSvg,
        string backgroundColor,
        string diagnostic,
        CancellationToken cancellationToken = default)
        => RenderCore(canonicalSvg, backgroundColor, diagnostic, 0, cancellationToken);

    public static RuntimeIconRenderResult RenderThemed(
        byte[] canonicalSvg,
        string backgroundColor,
        string diagnostic,
        CancellationToken cancellationToken = default,
        byte[]? visualSourceSvg = null,
        byte[]? canonicalRuntimeRgba = null)
        => RenderCore(canonicalSvg, backgroundColor, diagnostic, ThemeContentInset, cancellationToken,
            visualSourceSvg, canonicalRuntimeRgba);

    public static string EnsureVisibleBackground(string preferredBackground, byte[] runtimeRgba)
    {
        ArgumentNullException.ThrowIfNull(runtimeRgba);
        if (runtimeRgba.Length != MaximumDecodedBytes)
            throw new ArgumentException("Runtime RGBA has an invalid byte length.", nameof(runtimeRgba));
        _ = ParseColor(preferredBackground);
        if (HasBackgroundSeparation(preferredBackground, runtimeRgba)) return preferredBackground;
        foreach (var candidate in NeutralBackgrounds)
            if (!candidate.Equals(preferredBackground, StringComparison.Ordinal)
                && HasBackgroundSeparation(candidate, runtimeRgba))
                return candidate;
        throw new InputValidationException("Runtime artwork cannot be separated from any supported neutral background.");
    }

    public static bool HasUsableSurfaceContrast(string surfaceColor, byte[] runtimeRgba)
        => HasContrastCoverage(surfaceColor, runtimeRgba, MinimumThemeSurfaceContrastRatio);

    private static bool HasContrastCoverage(string surfaceColor, byte[] runtimeRgba, double minimumRatio)
    {
        ArgumentNullException.ThrowIfNull(runtimeRgba);
        if (runtimeRgba.Length != MaximumDecodedBytes)
            throw new ArgumentException("Runtime RGBA has an invalid byte length.", nameof(runtimeRgba));
        var surface = ParseColor(surfaceColor);
        var surfaceLuminance = RelativeLuminance(surface.Red, surface.Green, surface.Blue);
        var significant = 0;
        var contrasting = 0;
        for (var offset = 0; offset < runtimeRgba.Length; offset += 4)
        {
            var alphaByte = runtimeRgba[offset + 3];
            if (alphaByte < 240) continue;
            significant++;
            var alpha = alphaByte / 255d;
            var red = (byte)Math.Round(runtimeRgba[offset] * alpha + surface.Red * (1 - alpha));
            var green = (byte)Math.Round(runtimeRgba[offset + 1] * alpha + surface.Green * (1 - alpha));
            var blue = (byte)Math.Round(runtimeRgba[offset + 2] * alpha + surface.Blue * (1 - alpha));
            var foregroundLuminance = RelativeLuminance(red, green, blue);
            var contrast = (Math.Max(surfaceLuminance, foregroundLuminance) + 0.05)
                / (Math.Min(surfaceLuminance, foregroundLuminance) + 0.05);
            if (contrast >= minimumRatio) contrasting++;
        }
        return significant > 0 && contrasting >= Math.Ceiling(significant * 0.9);
    }

    private static bool HasBackgroundSeparation(string backgroundColor, IReadOnlyList<byte> rgba)
    {
        var background = ParseColor(backgroundColor);
        for (var offset = 0; offset < rgba.Count; offset += 4)
        {
            if (rgba[offset + 3] < 128) continue;
            var alpha = rgba[offset + 3] / 255d;
            var red = (byte)Math.Round(rgba[offset] * alpha + background.Red * (1 - alpha));
            var green = (byte)Math.Round(rgba[offset + 1] * alpha + background.Green * (1 - alpha));
            var blue = (byte)Math.Round(rgba[offset + 2] * alpha + background.Blue * (1 - alpha));
            if (red != background.Red || green != background.Green || blue != background.Blue) return true;
        }
        return false;
    }

    public static bool HasEmbeddedCompositionSilhouette(byte[] runtimeRgba)
    {
        ArgumentNullException.ThrowIfNull(runtimeRgba);
        if (runtimeRgba.Length != MaximumDecodedBytes)
            throw new ArgumentException("Runtime RGBA has an invalid byte length.", nameof(runtimeRgba));
        var visible = new bool[Width * Height];
        var visiblePixels = 0;
        var opaquePixels = 0;
        var left = Width;
        var top = Height;
        var right = -1;
        var bottom = -1;
        for (var index = 0; index < visible.Length; index++)
        {
            var alpha = runtimeRgba[index * 4 + 3];
            if (alpha < 64) continue;
            visible[index] = true;
            visiblePixels++;
            if (alpha >= 240) opaquePixels++;
            var x = index % Width;
            var y = index / Width;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        if (visiblePixels == 0) return false;
        var boundsArea = checked((right - left + 1) * (bottom - top + 1));
        if (opaquePixels < boundsArea * 0.5) return false;

        var visited = new bool[visible.Length];
        var queue = new int[visible.Length];
        var largestComponent = 0;
        for (var start = 0; start < visible.Length; start++)
        {
            if (!visible[start] || visited[start]) continue;
            var head = 0;
            var tail = 0;
            queue[tail++] = start;
            visited[start] = true;
            void Enqueue(int neighbor)
            {
                if (!visible[neighbor] || visited[neighbor]) return;
                visited[neighbor] = true;
                queue[tail++] = neighbor;
            }
            while (head < tail)
            {
                var current = queue[head++];
                var x = current % Width;
                var y = current / Width;
                if (x > 0) Enqueue(current - 1);
                if (x + 1 < Width) Enqueue(current + 1);
                if (y > 0) Enqueue(current - Width);
                if (y + 1 < Height) Enqueue(current + Width);
            }
            largestComponent = Math.Max(largestComponent, tail);
        }
        return largestComponent >= Math.Ceiling(visiblePixels * 0.9);
    }

    private static RuntimeIconRenderResult RenderCore(
        byte[] canonicalSvg,
        string backgroundColor,
        string diagnostic,
        int contentInset,
        CancellationToken cancellationToken,
        byte[]? visualSourceSvg = null,
        byte[]? canonicalRuntimeRgba = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SvgNormalizer.ValidateCanonical(canonicalSvg, diagnostic);
        if (canonicalRuntimeRgba is not null && canonicalRuntimeRgba.Length != MaximumDecodedBytes)
            throw new InputValidationException($"Runtime raster reference for '{diagnostic}' has an invalid byte length.");
        using var bitmap = canonicalRuntimeRgba is null
            ? RenderCanonicalBitmap(ApplyRuntimePaintProfile(canonicalSvg), contentInset)
            : null;
        cancellationToken.ThrowIfCancellationRequested();

        var rgba = canonicalRuntimeRgba?.ToArray() ?? new byte[MaximumDecodedBytes];
        var visiblePixels = 0;
        var separatedPixels = 0;
        var background = ParseColor(backgroundColor);
        for (var y = 0; y < Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * Width + x) * 4;
                var pixel = bitmap is null
                    ? new SKColor(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3])
                    : bitmap.GetPixel(x, y);
                if (bitmap is not null)
                {
                    rgba[offset] = pixel.Red;
                    rgba[offset + 1] = pixel.Green;
                    rgba[offset + 2] = pixel.Blue;
                    rgba[offset + 3] = pixel.Alpha;
                }
                if (pixel.Alpha != 0)
                {
                    visiblePixels++;
                    var alpha = pixel.Alpha / 255d;
                    var red = (byte)Math.Round(pixel.Red * alpha + background.Red * (1 - alpha));
                    var green = (byte)Math.Round(pixel.Green * alpha + background.Green * (1 - alpha));
                    var blue = (byte)Math.Round(pixel.Blue * alpha + background.Blue * (1 - alpha));
                    if (red != background.Red || green != background.Green || blue != background.Blue)
                        separatedPixels++;
                }
            }
        }
        if (visiblePixels == 0)
            throw new InputValidationException($"Runtime rendering for '{diagnostic}' produced a blank image.");
        if (separatedPixels == 0)
            throw new InputValidationException($"Runtime rendering for '{diagnostic}' is fully covered by background color '{backgroundColor}'.");

        var png = DeterministicPng.EncodeRgba(Width, Height, rgba);
        using var reopened = DeterministicPng.DecodeAndValidate(png, Width, Height, diagnostic);
        // The RGBA buffer is the exact bounded canonical/source render used to
        // encode the PNG. Comparing the reopened PNG with that buffer proves
        // the same visual relation without parsing and rasterizing the SVG a
        // second time. The earlier source-to-canonical gate remains mandatory.
        var metrics = SvgVisualVerifier.CompareExpectedRgbaToRuntimeBitmap(
            rgba, reopened, backgroundColor);
        if (!metrics.Resolved)
            throw new InputValidationException($"Runtime rendering for '{diagnostic}' failed visual verification (mean difference {metrics.MeanAbsoluteChannelDifference}, material pixels {metrics.MateriallyDifferentPixels}).");
        return new RuntimeIconRenderResult(png, DeterministicPng.Sha256(png), metrics);
    }

    private static double RelativeLuminance(byte red, byte green, byte blue)
    {
        static double Linear(byte component)
        {
            var value = component / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
    }

    public static RuntimeIconRenderResult RenderReviewedBadgeComposition(
        byte[] canonicalSvg,
        string matteColor,
        string previewBackgroundColor,
        string diagnostic,
        CancellationToken cancellationToken = default,
        string compositionMode = EnclosedMatteComposition)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var composed = CreateReviewedBadgeBitmap(canonicalSvg, matteColor,
            diagnostic, cancellationToken, compositionMode);
        var rgba = new byte[MaximumDecodedBytes];
        var index = 0;
        foreach (var pixel in composed.Pixels)
        {
            rgba[index++] = pixel.Red;
            rgba[index++] = pixel.Green;
            rgba[index++] = pixel.Blue;
            rgba[index++] = pixel.Alpha;
        }
        var png = DeterministicPng.EncodeRgba(Width, Height, rgba);
        using var reopened = DeterministicPng.DecodeAndValidate(png, Width, Height, diagnostic);
        var metrics = SvgVisualVerifier.CompareExpectedRgbaToRuntimeBitmap(
            rgba, reopened, previewBackgroundColor);
        if (!metrics.Resolved)
            throw new InputValidationException($"Reviewed badge rendering for '{diagnostic}' failed visual verification (mean difference {metrics.MeanAbsoluteChannelDifference}, material pixels {metrics.MateriallyDifferentPixels}).");
        return new RuntimeIconRenderResult(png, DeterministicPng.Sha256(png), metrics);
    }

    internal static SKBitmap CreateReviewedBadgeBitmap(
        byte[] canonicalSvg,
        string matteColor,
        string diagnostic,
        CancellationToken cancellationToken = default,
        string compositionMode = EnclosedMatteComposition)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SvgNormalizer.ValidateCanonical(canonicalSvg, diagnostic);
        if (compositionMode is not (EnclosedMatteComposition or SquareFrameComposition))
            throw new InputValidationException($"Unsupported badge composition '{compositionMode}' for '{diagnostic}'.");
        var renderSvg = ApplyRuntimePaintProfile(canonicalSvg);
        using var source = RenderCanonicalBitmap(renderSvg, ThemeContentInset
            + (compositionMode == SquareFrameComposition ? SquareFrameWidth : 0));
        return compositionMode == SquareFrameComposition
            ? ApplySquareFrame(source, ParseColor(matteColor), diagnostic, cancellationToken)
            : ApplyEnclosedMatte(source, ParseColor(matteColor), diagnostic, cancellationToken);
    }

    private static SKBitmap ApplySquareFrame(
        SKBitmap source, SKColor matte, string diagnostic, CancellationToken cancellationToken)
    {
        // An explicit source mapping authorizes the matte; geometry alone never
        // opts a logo into this operation. Reject a changed/non-square source.
        const int edge = ThemeContentInset + SquareFrameWidth;
        const int end = Width - edge - 1;
        for (var position = edge; position <= end; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.GetPixel(position, edge).Alpha != 255
                || source.GetPixel(position, end).Alpha != 255
                || source.GetPixel(edge, position).Alpha != 255
                || source.GetPixel(end, position).Alpha != 255)
                throw new InputValidationException($"Reviewed square badge '{diagnostic}' must have an opaque, square outer contour.");
        }
        using var output = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var brandPixels = 0;
        for (var y = 0; y < Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                if ((x < edge || x > end || y < edge || y > end) && pixel.Alpha != 0)
                    throw new InputValidationException($"Reviewed square badge '{diagnostic}' has artwork outside its square contour.");
                if (x < ThemeContentInset || x >= Width - ThemeContentInset
                    || y < ThemeContentInset || y >= Height - ThemeContentInset)
                {
                    output.SetPixel(x, y, SKColors.Transparent);
                    continue;
                }
                var alpha = pixel.Alpha / 255d;
                output.SetPixel(x, y, new SKColor(
                    (byte)Math.Round(pixel.Red * alpha + matte.Red * (1 - alpha)),
                    (byte)Math.Round(pixel.Green * alpha + matte.Green * (1 - alpha)),
                    (byte)Math.Round(pixel.Blue * alpha + matte.Blue * (1 - alpha)), 255));
                if (pixel.Alpha == 255 && pixel != matte) brandPixels++;
            }
        }
        if (brandPixels == 0)
            throw new InputValidationException($"Reviewed square badge '{diagnostic}' contains no distinct opaque brand artwork.");
        return output.Copy();
    }

    private static SKBitmap RenderCanonicalBitmap(byte[] svg, int contentInset)
    {
        if (contentInset < 0 || contentInset * 2 >= Width || contentInset * 2 >= Height)
            throw new ArgumentOutOfRangeException(nameof(contentInset));
        var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        Draw(svg, canvas, new SKRect(contentInset, contentInset,
            Width - contentInset, Height - contentInset));
        canvas.Flush();
        return bitmap;
    }

    private static SKBitmap ApplyEnclosedMatte(
        SKBitmap source,
        SKColor matte,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        var width = source.Width;
        var height = source.Height;
        var exterior = new bool[checked(width * height)];
        var queue = new Queue<int>();
        void AddExterior(int x, int y)
        {
            var offset = y * width + x;
            if (exterior[offset] || source.GetPixel(x, y).Alpha != 0) return;
            exterior[offset] = true;
            queue.Enqueue(offset);
        }
        for (var x = 0; x < width; x++)
        {
            AddExterior(x, 0);
            AddExterior(x, height - 1);
        }
        for (var y = 1; y < height - 1; y++)
        {
            AddExterior(0, y);
            AddExterior(width - 1, y);
        }
        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = queue.Dequeue();
            var x = offset % width;
            var y = offset / width;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var adjacentX = x + dx;
                    var adjacentY = y + dy;
                    if ((uint)adjacentX < (uint)width && (uint)adjacentY < (uint)height)
                        AddExterior(adjacentX, adjacentY);
                }
        }

        var interiorTransparentPixels = 0;
        var exteriorTransparentPixels = 0;
        var brandPixels = 0;
        using var output = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var sourcePixel = source.GetPixel(x, y);
                var offset = y * width + x;
                SKColor outputPixel;
                if (sourcePixel.Alpha == 0)
                {
                    if (exterior[offset])
                    {
                        exteriorTransparentPixels++;
                        outputPixel = SKColors.Transparent;
                    }
                    else
                    {
                        interiorTransparentPixels++;
                        outputPixel = new SKColor(matte.Red, matte.Green, matte.Blue, 255);
                    }
                }
                else if (sourcePixel.Alpha < 255 && IsNearestTransparentRegionInterior(source, exterior, x, y))
                {
                    var alpha = sourcePixel.Alpha / 255d;
                    outputPixel = new SKColor(
                        (byte)Math.Round(sourcePixel.Red * alpha + matte.Red * (1 - alpha)),
                        (byte)Math.Round(sourcePixel.Green * alpha + matte.Green * (1 - alpha)),
                        (byte)Math.Round(sourcePixel.Blue * alpha + matte.Blue * (1 - alpha)),
                        255);
                }
                else
                {
                    outputPixel = sourcePixel;
                    if (sourcePixel.Alpha == 255
                        && (sourcePixel.Red != matte.Red || sourcePixel.Green != matte.Green || sourcePixel.Blue != matte.Blue))
                        brandPixels++;
                }
                output.SetPixel(x, y, outputPixel);
            }
        }
        if (interiorTransparentPixels == 0)
            throw new InputValidationException($"Reviewed badge composition for '{diagnostic}' contains no enclosed transparent detail.");
        if (exteriorTransparentPixels == 0)
            throw new InputValidationException($"Reviewed badge composition for '{diagnostic}' has no transparent exterior.");
        if (brandPixels == 0)
            throw new InputValidationException($"Reviewed badge composition for '{diagnostic}' contains no opaque brand-colored artwork.");
        return output.Copy();
    }

    private static bool IsNearestTransparentRegionInterior(
        SKBitmap source,
        IReadOnlyList<bool> exterior,
        int x,
        int y)
    {
        for (var radius = 1; radius <= 3; radius++)
        {
            var foundInterior = false;
            var foundExterior = false;
            for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                    var adjacentX = x + dx;
                    var adjacentY = y + dy;
                    if ((uint)adjacentX >= (uint)source.Width || (uint)adjacentY >= (uint)source.Height
                        || source.GetPixel(adjacentX, adjacentY).Alpha != 0)
                        continue;
                    if (exterior[adjacentY * source.Width + adjacentX]) foundExterior = true;
                    else foundInterior = true;
                }
            // Prefer preserving exterior antialiasing in an ambiguous thin
            // contour rather than introducing a light halo around the badge.
            if (foundExterior) return false;
            if (foundInterior) return true;
        }
        return false;
    }

    internal static byte[] ApplyRuntimePaintProfile(byte[] canonicalSvg)
    {
        XDocument document;
        using (var stream = new MemoryStream(canonicalSvg, writable: false))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024
        }))
            document = XDocument.Load(reader, LoadOptions.None);
        foreach (var path in document.Root!.Elements().Where(x => x.Name.LocalName == "path"))
            if (path.Attribute("fill") is null)
                path.SetAttributeValue("fill", "#FFFFFF");
        using var result = new MemoryStream();
        using (var writer = XmlWriter.Create(result, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None
        }))
            document.Save(writer);
        return result.ToArray();
    }

    internal static void Draw(byte[] svgBytes, SKCanvas canvas, SKRect target)
    {
        using var svg = new SKSvg();
        using var stream = new MemoryStream(svgBytes, writable: false);
        var picture = svg.Load(stream)
            ?? throw new InputValidationException("Bounded SVG renderer could not load canonical artwork.");
        var bounds = picture.CullRect;
        if (!float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0)
            throw new InputValidationException("Bounded SVG renderer produced invalid geometry bounds.");
        var scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
        var x = target.Left + (target.Width - bounds.Width * scale) / 2;
        var y = target.Top + (target.Height - bounds.Height * scale) / 2;
        canvas.Save();
        canvas.ClipRect(target);
        canvas.Translate(x, y);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static SKColor ParseColor(string value)
    {
        if (value.Length != 7 || value[0] != '#')
            throw new InputValidationException($"Runtime rendering received invalid background color '{value}'.");
        try
        {
            return new SKColor(
                Convert.ToByte(value.Substring(1, 2), 16),
                Convert.ToByte(value.Substring(3, 2), 16),
                Convert.ToByte(value.Substring(5, 2), 16));
        }
        catch (FormatException)
        {
            throw new InputValidationException($"Runtime rendering received invalid background color '{value}'.");
        }
    }
}
