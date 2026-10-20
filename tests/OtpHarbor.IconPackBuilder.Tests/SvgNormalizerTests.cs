using System.Text;
using System.Xml.Linq;
using OtpHarbor.IconPackBuilder.Normalization;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class SvgNormalizerTests
{
    [Fact]
    public void PromotesStyledFullBackgroundAndPreservesForegroundPaint()
    {
        var result = Normalize("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <circle cx="50" cy="50" r="50" style="fill:red"/>
              <path d="M25 25h50v50H25z" style="fill:#fff"/>
            </svg>
            """);

        Assert.Equal("#FF0000", result.BackgroundColor);
        Assert.Contains("promoted-full-logo-background", result.Operations);
        var document = Parse(result);
        var path = Assert.Single(document.Root!.Elements());
        Assert.Equal("#FFFFFF", path.Attribute("fill")!.Value);
        Assert.Null(path.Attribute("style"));
    }

    [Fact]
    public void ExpandsUseAndRepairsWrappedTransformValue()
    {
        var result = Normalize("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <defs><path id="shape" d="M0 0h1v1z" fill="#123456"/></defs>
              <use href="#shape" transform='transform="translate(2,3)"'/>
            </svg>
            """);

        var path = Assert.Single(Parse(result).Root!.Elements());
        Assert.Equal("translate(2,3)", path.Attribute("transform")!.Value);
        Assert.Null(path.Attribute("href"));
        Assert.Contains("expanded-use", result.Operations);
    }

    [Fact]
    public void DerivesViewBoxConvertsShapesAndResolvesCssAndGradientPaint()
    {
        var result = Normalize("""
            <svg xmlns="http://www.w3.org/2000/svg" width="20px" height="10px">
              <style>.brand { fill: url(#brand); }</style>
              <defs><linearGradient id="brand"><stop stop-color="#000000"/><stop style="stop-color:#FFFFFF"/></linearGradient></defs>
              <rect class="brand" x="1" y="1" width="18" height="8"/>
            </svg>
            """);

        var document = Parse(result);
        Assert.Equal("0 0 24 24", document.Root!.Attribute("viewBox")!.Value);
        Assert.NotEmpty(document.Root.Elements());
        Assert.Contains("derived-viewbox", result.Operations);
        Assert.Contains("converted-basic-shapes", result.Operations);
        Assert.Contains("resolved-css-classes", result.Operations);
        Assert.Contains("flattened-gradient-paint", result.Operations);
        Assert.Contains("raster-flattened-at-96px", result.Operations);
        SvgNormalizer.ValidateCanonical(result.Svg, "fixture.svg");
    }

    [Fact]
    public void SelectsLightNeutralForDarkColoredForegroundOnFallback()
    {
        var result = Normalize("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <path d="M0 0h10v10z" fill="#4724CC"/>
            </svg>
            """);

        Assert.Equal("#FFFFFF", result.BackgroundColor);
        Assert.Contains("selected-light-contrast-background", result.Operations);
    }

    [Fact]
    public void SelectsLightNeutralForBalancedBrandColorAndWhitePalette()
    {
        var result = Normalize("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <path d="M0 0h10v10z" fill="#E40046"/>
              <path d="M3 3h4v4H3z" fill="#FFFFFF"/>
            </svg>
            """);

        Assert.Equal("#000000", result.BackgroundColor);
        Assert.Contains("selected-dark-contrast-background", result.Operations);
    }

    [Fact]
    public void ReplacesForegroundMatchingBackgroundUsingAllRetainedPaints()
    {
        var result = SvgNormalizer.Normalize(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <path d="M2 2h20v20H2z" fill="#D64027"/>
            </svg>
            """), "#D64027");

        Assert.Equal("#FFFFFF", result.BackgroundColor);
        Assert.Equal(["#D64027"], result.ForegroundColors);
        Assert.Contains("selected-light-contrast-background", result.Operations);
        var runtime = RuntimeIconRenderer.Render(result.Svg, result.BackgroundColor, "matching-background");
        Assert.True(runtime.VisualMetrics.Resolved);
        Assert.Equal(0, runtime.VisualMetrics.MateriallyDifferentPixels);
    }

    [Fact]
    public void MaterializesImplicitBlackForegroundForWhiteTile()
    {
        var result = SvgNormalizer.Normalize(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <path d="M1 1h8v8H1z"/>
            </svg>
            """), "#FFFFFF", materializeImplicitBlackFill: true);

        Assert.Equal("#000000", Assert.Single(Parse(result).Root!.Elements()).Attribute("fill")!.Value);
    }

    [Fact]
    public void VisualVerifierRejectsMateriallyWrongPaint()
    {
        var source = Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M0 0h24v24H0z" fill="#FF0000"/></svg>
            """);
        var wrong = Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M0 0h24v24H0z" fill="#0000FF"/></svg>
            """);

        var metrics = SvgVisualVerifier.Compare(source, wrong, "#FFFFFF");

        Assert.False(metrics.Resolved);
        Assert.True(metrics.MeanAbsoluteChannelDifference > 4);
        Assert.True(metrics.MateriallyDifferentPixels > metrics.TotalPixels / 10);
    }

    [Fact]
    public void VisualVerifierRejectsClippedCanonicalGeometry()
    {
        var source = Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M2 2h20v20H2z" fill="#FFFFFF"/></svg>
            """);
        var clipped = Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M20 20h20v20H20z" fill="#FFFFFF"/></svg>
            """);

        var metrics = SvgVisualVerifier.Compare(source, clipped, "#334155");

        Assert.False(metrics.Resolved);
        Assert.True(metrics.MateriallyDifferentPixels > 0);
    }

    [Fact]
    public void RuntimeRendererIsDeterministicTransparentAndUsesWhiteDefaultForeground()
    {
        var svg = Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M4 4h16v16H4z"/></svg>
            """);

        var first = RuntimeIconRenderer.Render(svg, "#334155", "fixture");
        var second = RuntimeIconRenderer.Render(svg, "#334155", "fixture");

        Assert.Equal(first.Png, second.Png);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.True(first.Png.Length < 16 * 1024, $"Expected compressed PNG, got {first.Png.Length} bytes.");
        using var bitmap = DeterministicPng.DecodeAndValidate(first.Png, 128, 128, "fixture.png");
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        var center = bitmap.GetPixel(64, 64);
        Assert.Equal(255, center.Alpha);
        Assert.Equal(255, center.Red);
        Assert.Equal(255, center.Green);
        Assert.Equal(255, center.Blue);
        Assert.True(first.VisualMetrics.Resolved);
    }

    [Fact]
    public void RuntimeRendererPreservesExplicitMulticolorFills()
    {
        var svg = Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <path d="M0 0h12v24H0z" fill="#FF0000"/>
              <path d="M12 0h12v24H12z" fill="#0000FF"/>
            </svg>
            """);

        var rendered = RuntimeIconRenderer.Render(svg, "#FFFFFF", "multicolor");

        using var bitmap = DeterministicPng.DecodeAndValidate(rendered.Png, 128, 128, "multicolor.png");
        Assert.Equal(new SkiaSharp.SKColor(255, 0, 0, 255), bitmap.GetPixel(16, 64));
        Assert.Equal(new SkiaSharp.SKColor(0, 0, 255, 255), bitmap.GetPixel(112, 64));
    }

    [Fact]
    public void RuntimeRendererRejectsBlankGeometry()
    {
        var svg = Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M0 0"/></svg>
            """);

        Assert.Throws<InputValidationException>(() => RuntimeIconRenderer.Render(svg, "#334155", "blank"));
    }

    [Fact]
    public void RuntimeRendererRejectsForegroundCoveredByBackground()
    {
        var svg = Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M0 0h24v24H0z" fill="#112233"/></svg>
            """);

        var exception = Assert.Throws<InputValidationException>(
            () => RuntimeIconRenderer.Render(svg, "#112233", "covered"));

        Assert.Contains("covered", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeRendererHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => RuntimeIconRenderer.Render(
            Encoding.UTF8.GetBytes(TestData.Svg),
            "#334155",
            "cancelled",
            cancellation.Token));
    }

    [Fact]
    public void RuntimeGateCoversTransformedNon24AndPromotedBackgroundArtwork()
    {
        var normalized = SvgNormalizer.Normalize(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="10 20 80 40">
              <rect x="10" y="20" width="80" height="40" fill="#112233"/>
              <g transform="translate(20 20)">
                <path d="M0 0h20v20H0z" fill="#E40046"/>
                <path d="M30 0h20v20H30z" fill="#00AEEF"/>
              </g>
            </svg>
            """), "#334155");

        var runtime = RuntimeIconRenderer.Render(
            normalized.Svg,
            normalized.BackgroundColor,
            "transformed-non-24-background");

        Assert.Equal("#112233", normalized.BackgroundColor);
        Assert.Contains("promoted-full-logo-background", normalized.Operations);
        Assert.True(runtime.VisualMetrics.Resolved);
        Assert.Equal(0, runtime.VisualMetrics.MateriallyDifferentPixels);
    }

    [Fact]
    public void RuntimeGateAcceptsCanonicalRasterFlattenedFallback()
    {
        var normalized = SvgNormalizer.Normalize(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" width="20" height="10">
              <defs><linearGradient id="g"><stop stop-color="#FF0000"/><stop offset="1" stop-color="#0000FF"/></linearGradient></defs>
              <ellipse cx="10" cy="5" rx="9" ry="4" fill="url(#g)"/>
            </svg>
            """), "#FFFFFF");

        var runtime = RuntimeIconRenderer.Render(normalized.Svg, normalized.BackgroundColor, "raster-fallback");

        Assert.Contains(normalized.Operations, operation => operation.StartsWith("raster-flattened-at-", StringComparison.Ordinal));
        Assert.True(runtime.VisualMetrics.Resolved);
        var paints = Parse(normalized).Root!.Elements()
            .SelectMany(path => new[] { (string?)path.Attribute("fill"), (string?)path.Attribute("stroke") })
            .Where(paint => paint is not null && paint != "none");
        Assert.DoesNotContain(normalized.BackgroundColor, paints);
    }

    private static SvgNormalizationResult Normalize(string svg) =>
        SvgNormalizer.Normalize(Encoding.UTF8.GetBytes(svg), "#334155");

    private static XDocument Parse(SvgNormalizationResult result) =>
        XDocument.Parse(Encoding.UTF8.GetString(result.Svg));
}
