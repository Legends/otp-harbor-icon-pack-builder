using System.Text;
using OtpHarbor.IconPackBuilder.Normalization;
using OtpHarbor.IconPackBuilder.Packaging;
using SkiaSharp;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class SquareBadgeTests
{
    private const string Badge = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
          <path fill="#123456" fill-rule="evenodd" d="M0 0h24v24H0zM8 8h8v8H8z"/>
        </svg>
        """;

    [Theory]
    [InlineData("square", "Square", "#123456", false)]
    [InlineData("1and1", "1&1", "#003D8F", true)]
    [InlineData("aidungeon", "AI Dungeon", "#000000", true)]
    public async Task ReviewedSquareFramePreservesPaintSharpCornersInsetAndWhiteDetailsInBothThemes(
        string sourceId, string name, string color, bool useDefaultMappings)
    {
        // Synthetic geometry exercises the real mappings without copying upstream artwork.
        var record = new SourceRecord("simple-icons", sourceId, name, [name],
            $"icons/{sourceId}.svg", Encoding.UTF8.GetBytes(Badge.Replace("#123456", color)), "default",
            new SortedDictionary<string, string?>(StringComparer.Ordinal));
        var mappings = new MappingSet([], new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, SourceOverride>(), null,
            new Dictionary<string, AppearanceCompositionOverride>
            {
                ["square"] = new("simple-icons", "square", RuntimeIconRenderer.SquareFrameComposition,
                    "#FFFFFF", "Reviewed synthetic square badge with white detail and frame.")
            });
        if (useDefaultMappings) mappings = await MappingLoader.LoadDefaultAsync();
        var catalog = TestData.Catalog("simple-icons", record);
        var result = new BrandMerger().Merge([catalog], mappings, formatVersion: 3);
        var brand = Assert.Single(result.Document.Brands);
        var light = brand.Appearances!.LightTheme;
        var dark = brand.Appearances.DarkTheme;
        Assert.Equal(light.Asset.Path, dark.Asset.Path);
        Assert.All(new[] { light, dark }, appearance =>
        {
            Assert.Equal("complete-icon", appearance.AssetRole);
            Assert.Equal("embedded", appearance.Background.Mode);
            Assert.Null(appearance.Background.Color);
            Assert.Equal("reviewed-badge-composition", appearance.Selection);
            Assert.Equal("simple-icons", appearance.Source.Provider);
            Assert.Equal(sourceId, appearance.Source.SourceId);
        });
        var png = Assert.Single(result.RuntimeIcons!).Value;
        using var bitmap = DeterministicPng.DecodeAndValidate(png, 128, 128, "square badge");
        for (var y = 0; y < 128; y++)
        for (var x = 0; x < 128; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (x < 8 || x >= 120 || y < 8 || y >= 120)
                Assert.Equal(0, pixel.Alpha);
            else if (x < 12 || x >= 116 || y < 12 || y >= 116)
                Assert.Equal(SKColors.White, pixel);
            else
                Assert.Equal(255, pixel.Alpha);
        }
        Assert.Equal(SKColors.White, bitmap.GetPixel(64, 64));
        var brandColor = SKColor.Parse(color);
        Assert.Equal(brandColor, bitmap.GetPixel(12, 12));
        Assert.Equal(brandColor, bitmap.GetPixel(115, 115));
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha == 255
            && pixel.Red > brandColor.Red && pixel.Red < 255); // Preserve antialiased lettering over the matte.
        var reference = result.RuntimeVisualReferences!.First();
        Assert.Equal(RuntimeIconRenderer.SquareFrameComposition, reference.CompositionMode);
        Assert.True(SvgVisualVerifier.CompareCanonicalToReviewedBadgeRuntimePng(
            reference.CanonicalSvg, png, "#FFFFFF", RuntimeIconRenderer.DarkThemeSurface,
            RuntimeIconRenderer.SquareFrameComposition).Resolved);

        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var firstPath = fixture.FilePath("first.otphicons");
        var secondPath = fixture.FilePath("second.otphicons");
        await PackSerializer.WriteAsync(firstPath, result);
        var reopened = await PackReader.ReadAndValidateAsync(firstPath);
        PackReader.ValidateV3RuntimeVisuals(reopened, result.RuntimeVisualReferences!);
        Assert.Equal(png, reopened.Files[light.Asset.Path]);
        var repeated = new BrandMerger().Merge([catalog], mappings, formatVersion: 3);
        await PackSerializer.WriteAsync(secondPath, repeated);
        Assert.Equal(await File.ReadAllBytesAsync(firstPath), await File.ReadAllBytesAsync(secondPath));
    }

    [Theory]
    [InlineData("<circle cx='12' cy='12' r='12' fill='#123456'/>", "opaque, square outer contour")]
    [InlineData("<rect width='24' height='24' rx='2' fill='#123456'/>", "opaque, square outer contour")]
    [InlineData("<rect y='4' width='24' height='16' fill='#123456'/>", "opaque, square outer contour")]
    [InlineData("<path d='M0 0h10v2h4V0h10v24H0z' fill='#123456'/>", "opaque, square outer contour")]
    [InlineData("<rect width='24' height='24' fill='#FFFFFF'/>", "no distinct opaque brand artwork")]
    public void RejectsUnreviewedGeometryOrMissingBrandPaint(string shape, string reason)
    {
        var svg = Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'>{shape}</svg>");
        var canonical = SvgNormalizer.Normalize(svg, "#10213F", preserveEmbeddedBackground: true).Svg;
        var exception = Assert.Throws<InputValidationException>(() =>
            RuntimeIconRenderer.RenderReviewedBadgeComposition(canonical, "#FFFFFF", "#10213F",
                "simple-icons/changed-square", compositionMode: RuntimeIconRenderer.SquareFrameComposition));
        Assert.Contains(reason, exception.Message);
        Assert.Contains("simple-icons/changed-square", exception.Message);
    }

    [Fact]
    public void RejectsUnknownCompositionAndHonorsCancellation()
    {
        var svg = Encoding.UTF8.GetBytes(Badge);
        Assert.Throws<InputValidationException>(() => RuntimeIconRenderer.RenderReviewedBadgeComposition(
            svg, "#FFFFFF", "#10213F", "square", compositionMode: "unknown"));
        Assert.Throws<OperationCanceledException>(() => RuntimeIconRenderer.RenderReviewedBadgeComposition(
            svg, "#FFFFFF", "#10213F", "square", new CancellationToken(true),
            RuntimeIconRenderer.SquareFrameComposition));
    }
}
