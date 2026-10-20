using System.Text;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;
using Svg.Skia;

namespace OtpHarbor.IconPackBuilder.Normalization;

public sealed record RuntimeIconRenderResult(byte[] Png, string Sha256, SvgVisualMetrics VisualMetrics);

public static class RuntimeIconRenderer
{
    public const int Width = 128;
    public const int Height = 128;
    public const int MaximumDecodedBytes = Width * Height * 4;
    public const string MediaType = "image/png";
    public const string RenderProfile = "otp-harbor-foreground-v1";

    public static RuntimeIconRenderResult Render(
        byte[] canonicalSvg,
        string backgroundColor,
        string diagnostic,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SvgNormalizer.ValidateCanonical(canonicalSvg, diagnostic);
        var renderSvg = ApplyRuntimePaintProfile(canonicalSvg);
        using var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            Draw(renderSvg, canvas, new SKRect(0, 0, Width, Height));
            canvas.Flush();
        }
        cancellationToken.ThrowIfCancellationRequested();

        var rgba = new byte[MaximumDecodedBytes];
        var visiblePixels = 0;
        var separatedPixels = 0;
        var background = ParseColor(backgroundColor);
        for (var y = 0; y < Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var offset = (y * Width + x) * 4;
                rgba[offset] = pixel.Red;
                rgba[offset + 1] = pixel.Green;
                rgba[offset + 2] = pixel.Blue;
                rgba[offset + 3] = pixel.Alpha;
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
            throw new InputValidationException($"Runtime rendering for '{diagnostic}' is fully covered by its background color.");

        var png = DeterministicPng.EncodeRgba(Width, Height, rgba);
        using var reopened = DeterministicPng.DecodeAndValidate(png, Width, Height, diagnostic);
        var metrics = SvgVisualVerifier.CompareCanonicalToRuntimePng(canonicalSvg, png, backgroundColor);
        if (!metrics.Resolved)
            throw new InputValidationException($"Runtime rendering for '{diagnostic}' failed visual verification (mean difference {metrics.MeanAbsoluteChannelDifference}, material pixels {metrics.MateriallyDifferentPixels}).");
        return new RuntimeIconRenderResult(png, DeterministicPng.Sha256(png), metrics);
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
