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

        Assert.Equal("#FFFFFF", result.BackgroundColor);
        Assert.Contains("selected-light-contrast-background", result.Operations);
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

    private static SvgNormalizationResult Normalize(string svg) =>
        SvgNormalizer.Normalize(Encoding.UTF8.GetBytes(svg), "#334155");

    private static XDocument Parse(SvgNormalizationResult result) =>
        XDocument.Parse(Encoding.UTF8.GetString(result.Svg));
}
