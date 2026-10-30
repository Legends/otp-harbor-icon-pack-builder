using OtpHarbor.IconPackBuilder.Normalization;
using OtpHarbor.IconPackBuilder.Packaging;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class MergeTests
{
    [Fact]
    public void ExplicitCanonicalMappingsMergeProviderRecordsAndAliases()
    {
        var mapping = new CanonicalBrandMapping("amazon-web-services", "Amazon Web Services", ["AWS", "Amazon AWS"],
            [new("aegis", "AWS"), new("simple-icons", "amazonwebservices")]);
        var catalogs = new[]
        {
            TestData.Catalog("aegis", TestData.Record("aegis", "AWS", "AWS", "aws.amazon.com")),
            TestData.Catalog("simple-icons", TestData.Record("simple-icons", "amazonwebservices", "Amazon Web Services", "Amazon AWS"))
        };

        var result = new BrandMerger().Merge(catalogs, TestData.Mappings(mapping));

        var brand = Assert.Single(result.Document.Brands);
        Assert.Equal("amazon-web-services", brand.Id);
        Assert.Contains("AWS", brand.IssuerAliases);
        Assert.Contains("Amazon AWS", brand.IssuerAliases);
        Assert.Contains("aws.amazon.com", brand.IssuerAliases);
        Assert.Equal(2, brand.Sources.Count);
        Assert.Equal(1, result.Summary.BrandsMerged);
    }

    [Fact]
    public void ReportsDeterministicCanonicalCatalogProgress()
    {
        var updates = new List<CatalogBuildProgress>();

        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis",
                TestData.Record("aegis", "GitHub", "GitHub"),
                TestData.Record("aegis", "GitLab", "GitLab"))],
            MappingSet.Empty,
            progress: updates.Add);

        Assert.Equal(2, result.Document.Brands.Count);
        Assert.Equal(0, updates[0].CompletedBrands);
        Assert.Equal(2, updates[0].TotalBrands);
        Assert.Equal(2, updates[^1].CompletedBrands);
        Assert.All(updates, update => Assert.Equal(2, update.TotalBrands));
        Assert.True(updates.Zip(updates.Skip(1)).All(pair =>
            pair.First.CompletedBrands <= pair.Second.CompletedBrands));
    }

    [Fact]
    public void AmazonAndAwsRemainSeparate()
    {
        var mappings = TestData.Mappings(
            new("amazon", "Amazon", ["Amazon"], [new("simple-icons", "amazon")]),
            new("amazon-web-services", "Amazon Web Services", ["AWS"], [new("simple-icons", "amazonwebservices")]));
        var result = new BrandMerger().Merge([
            TestData.Catalog("simple-icons",
                TestData.Record("simple-icons", "amazon", "Amazon"),
                TestData.Record("simple-icons", "amazonwebservices", "Amazon Web Services", "AWS"))], mappings);
        Assert.Equal(["amazon", "amazon-web-services"], result.Document.Brands.Select(x => x.Id));
    }

    [Fact]
    public void MicrosoftAndMicrosoft365RemainSeparate()
    {
        var mappings = TestData.Mappings(
            new("microsoft", "Microsoft", ["Microsoft"], [new("aegis", "Microsoft")]),
            new("microsoft-365", "Microsoft 365", ["Office 365", "M365"], [new("aegis", "Office 365")]));
        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis",
                TestData.Record("aegis", "Microsoft", "Microsoft"),
                TestData.Record("aegis", "Office 365", "Office 365", "M365"))], mappings);
        Assert.Equal(["microsoft", "microsoft-365"], result.Document.Brands.Select(x => x.Id));
    }

    [Fact]
    public void AliasesAreNormalizedAndDeduplicatedWithinBrand()
    {
        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis", TestData.Record("aegis", "GitHub", "GitHub", " github ", "Git-Hub"))], MappingSet.Empty);
        var brand = Assert.Single(result.Document.Brands);
        Assert.Equal(2, brand.IssuerAliases.Count);
        Assert.Equal(2, result.Document.IssuerAliases.Count);
    }

    [Fact]
    public void AliasCollisionAcrossBrandsFailsRatherThanChoosing()
    {
        var catalog = TestData.Catalog("aegis",
            TestData.Record("aegis", "Foo", "Foo", "Shared Login"),
            TestData.Record("aegis", "Bar", "Bar", "shared-login"));
        var mappings = new MappingSet([], new Dictionary<string, IReadOnlyList<string>>
        {
            ["foo"] = ["Shared Login"],
            ["bar"] = ["shared-login"]
        }, new Dictionary<string, SourceOverride>());
        var exception = Assert.Throws<BuildConflictException>(() => new BrandMerger().Merge([catalog], mappings));
        var conflict = Assert.Single(exception.Conflicts);
        Assert.Equal("alias-collision", conflict.Type);
        Assert.Equal("shared login", conflict.Key);
    }

    [Fact]
    public void AmbiguousInferredAliasesAreSuppressedRatherThanMappedIncorrectly()
    {
        var catalog = TestData.Catalog("dashboard-icons",
            TestData.Record("dashboard-icons", "alpha", "Alpha", "Web Browser"),
            TestData.Record("dashboard-icons", "beta", "Beta", "web-browser"));

        var result = new BrandMerger().Merge([catalog], MappingSet.Empty);

        Assert.DoesNotContain(result.Document.IssuerAliases, x => x.Key == "web browser");
        Assert.Equal(1, result.Summary.SuppressedAliases);
    }

    [Fact]
    public void CanonicalIdCollisionFailsWithoutExplicitMapping()
    {
        var catalog = TestData.Catalog("aegis",
            TestData.Record("aegis", "A", "Zabka"), TestData.Record("aegis", "B", "Żabka"));
        var exception = Assert.Throws<BuildConflictException>(() => new BrandMerger().Merge([catalog], MappingSet.Empty));
        Assert.Contains(exception.Conflicts, x => x.Type == "canonicalization");
    }

    [Fact]
    public void ExplicitSourceOverrideWins()
    {
        var mapping = new CanonicalBrandMapping("github", "GitHub", ["GitHub"],
            [new("aegis", "GitHub"), new("simple-icons", "github")]);
        var mappings = new MappingSet([mapping], new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, SourceOverride> { ["github"] = new("simple-icons", "github") });
        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis", TestData.Record("aegis", "GitHub", "GitHub")),
            TestData.Catalog("simple-icons", TestData.Record("simple-icons", "github", "GitHub"))], mappings);
        Assert.Equal("simple-icons", Assert.Single(result.Document.Brands).SelectedSource.Provider);
    }

    [Fact]
    public void DefaultPreferenceIsDashboardThenAegisThenSimpleIcons()
    {
        var mapping = new CanonicalBrandMapping("github", "GitHub", ["GitHub"],
            [new("aegis", "GitHub"), new("dashboard-icons", "github"), new("simple-icons", "github")]);
        var result = new BrandMerger().Merge([
            TestData.Catalog("simple-icons", TestData.Record("simple-icons", "github", "GitHub")),
            TestData.Catalog("dashboard-icons", TestData.Record("dashboard-icons", "github", "GitHub")),
            TestData.Catalog("aegis", TestData.Record("aegis", "GitHub", "GitHub"))], TestData.Mappings(mapping));
        Assert.Equal("dashboard-icons", Assert.Single(result.Document.Brands).SelectedSource.Provider);
    }

    [Fact]
    public void CustomPreferenceIsHonored()
    {
        var mapping = new CanonicalBrandMapping("github", "GitHub", ["GitHub"],
            [new("aegis", "GitHub"), new("dashboard-icons", "github")]);
        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis", TestData.Record("aegis", "GitHub", "GitHub")),
            TestData.Catalog("dashboard-icons", TestData.Record("dashboard-icons", "github", "GitHub"))],
            TestData.Mappings(mapping), ["dashboard-icons", "aegis"]);
        Assert.Equal("dashboard-icons", Assert.Single(result.Document.Brands).SelectedSource.Provider);
    }

    [Fact]
    public void VersionTwoRepairsPreferredForegroundBackgroundCollisionBeforeRendering()
    {
        var mapping = new CanonicalBrandMapping("example", "Example", ["Example"],
            [new("aegis", "Example"), new("dashboard-icons", "example")]);
        var covered = RecordWithSvg("aegis", "Example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <path d="M4 4h16v16H4z" fill="#AA0000"/>
            </svg>
            """) with
        {
            Metadata = new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["nativeBrandColor"] = "#AA0000"
            }
        };
        var alternate = RecordWithSvg("dashboard-icons", "example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <path d="M4 4h16v16H4z" fill="#0066CC"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis", covered),
            TestData.Catalog("dashboard-icons", alternate)
        ], TestData.Mappings(mapping), ["aegis", "dashboard-icons"], formatVersion: 2);

        var brand = Assert.Single(result.Document.Brands);
        Assert.Equal("aegis", brand.SelectedSource.Provider);
        Assert.Equal("#FFFFFF", brand.BackgroundColor);
        Assert.NotNull(brand.RuntimeIcon);
    }

    [Fact]
    public void FullColorCandidateBeatsPreferredMonochromeBadge()
    {
        var mapping = new CanonicalBrandMapping("microsoft", "Microsoft", ["Microsoft"],
            [new("aegis", "Microsoft"), new("dashboard-icons", "microsoft")]);
        var monochrome = RecordWithSvg("aegis", "Microsoft", "Microsoft", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <rect width="10" height="10" fill="#737373"/>
              <path d="M2 2h6v6H2z" fill="#FFFFFF"/>
            </svg>
            """);
        var fullColor = RecordWithSvg("dashboard-icons", "microsoft", "Microsoft", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <rect width="10" height="10" fill="#F3F3F3"/>
              <path d="M1 1h3v3H1z" fill="#F35325"/>
              <path d="M6 1h3v3H6z" fill="#81BC06"/>
              <path d="M1 6h3v3H1z" fill="#05A6F0"/>
              <path d="M6 6h3v3H6z" fill="#FFBA08"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis", monochrome),
            TestData.Catalog("dashboard-icons", fullColor)], TestData.Mappings(mapping));

        var selected = Assert.Single(result.Document.Brands);
        Assert.Equal("dashboard-icons", selected.SelectedSource.Provider);
        Assert.Equal("#F3F3F3", selected.BackgroundColor);
        Assert.Equal("4", selected.SelectedSource.Metadata["normalizedForegroundColorCount"]);
    }

    [Fact]
    public void CanonicalVectorBeatsRasterFlattenedCandidate()
    {
        var mapping = new CanonicalBrandMapping("example", "Example", ["Example"],
            [new("aegis", "Example"), new("dashboard-icons", "example")]);
        var gradient = RecordWithSvg("aegis", "Example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <defs><linearGradient id="brand"><stop stop-color="#00AAFF"/><stop offset="1" stop-color="#00DD88"/></linearGradient></defs>
              <rect width="10" height="10" fill="url(#brand)"/>
            </svg>
            """);
        var vector = RecordWithSvg("dashboard-icons", "example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <path d="M1 1h8v8H1z" fill="#1185FE"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis", gradient),
            TestData.Catalog("dashboard-icons", vector)], TestData.Mappings(mapping));

        var selected = Assert.Single(result.Document.Brands);
        Assert.Equal("dashboard-icons", selected.SelectedSource.Provider);
        Assert.DoesNotContain("raster-flattened-at-",
            selected.SelectedSource.Metadata["visualNormalization"]);
    }

    [Fact]
    public void PreferredDashboardCandidateWinsBeforeCrossProviderVisualRanking()
    {
        var mapping = new CanonicalBrandMapping("example", "Example", ["Example"],
            [new("aegis", "Example"), new("dashboard-icons", "example")]);
        var badge = RecordWithSvg("aegis", "Example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <rect width="10" height="10" fill="#E1251B"/>
              <path d="M2 2h6v6H2z" fill="#FFFFFF"/>
            </svg>
            """);
        var neutral = RecordWithSvg("dashboard-icons", "example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <path d="M1 1h8v4H1z" fill="#000000"/>
              <path d="M1 6h8v3H1z" fill="#FFFFFF"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([
            TestData.Catalog("aegis", badge),
            TestData.Catalog("dashboard-icons", neutral)], TestData.Mappings(mapping));

        var selected = Assert.Single(result.Document.Brands);
        Assert.Equal("dashboard-icons", selected.SelectedSource.Provider);
    }

    [Fact]
    public void ExplicitChromaticForegroundBeatsImplicitBrandTile()
    {
        var mapping = new CanonicalBrandMapping("example", "Example", ["Example"],
            [new("dashboard-icons", "example"), new("simple-icons", "example")]);
        var explicitColor = RecordWithSvg("dashboard-icons", "example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">
              <path d="M1 1h8v8H1z" fill="#1185FE"/>
            </svg>
            """);
        var implicitForeground = new SourceRecord(
            "simple-icons", "example", "Example", ["Example"], "simple-icons/example.svg",
            System.Text.Encoding.UTF8.GetBytes(TestData.Svg), "default",
            new SortedDictionary<string, string?>(StringComparer.Ordinal) { ["nativeBrandColor"] = "#1185FE" });

        var result = new BrandMerger().Merge([
            TestData.Catalog("dashboard-icons", explicitColor),
            TestData.Catalog("simple-icons", implicitForeground)], TestData.Mappings(mapping));

        Assert.Equal("dashboard-icons", Assert.Single(result.Document.Brands).SelectedSource.Provider);
    }

    [Fact]
    public void BackgroundColorUsesOverrideThenSelectedNativeThenSelectedSvgThenFallback()
    {
        var aegis = RecordWithColors("aegis", "GitHub", "GitHub", native: null, extracted: "#AA0000");
        var simple = RecordWithColors("simple-icons", "github", "GitHub", native: "#00BB00", extracted: "#0000CC");
        var matches = new ProviderMatch[] { new("aegis", "GitHub"), new("simple-icons", "github") };

        var selectedAegis = new BrandMerger().Merge(
            [TestData.Catalog("aegis", aegis), TestData.Catalog("simple-icons", simple)],
            TestData.Mappings(new CanonicalBrandMapping("github", "GitHub", ["GitHub"], matches)));
        Assert.Equal("#AA0000", Assert.Single(selectedAegis.Document.Brands).BackgroundColor);

        var selectedSimple = new BrandMerger().Merge(
            [TestData.Catalog("aegis", aegis), TestData.Catalog("simple-icons", simple)],
            TestData.Mappings(new CanonicalBrandMapping("github", "GitHub", ["GitHub"], matches)),
            ["simple-icons", "aegis"]);
        Assert.Equal("#00BB00", Assert.Single(selectedSimple.Document.Brands).BackgroundColor);

        var overridden = new BrandMerger().Merge(
            [TestData.Catalog("aegis", aegis), TestData.Catalog("simple-icons", simple)],
            TestData.Mappings(new CanonicalBrandMapping("github", "GitHub", ["GitHub"], matches, "#DDEEFF")));
        Assert.Equal("#DDEEFF", Assert.Single(overridden.Document.Brands).BackgroundColor);
    }

    [Fact]
    public void DuplicateProviderSourceRecordsFail()
    {
        var record = TestData.Record("aegis", "Same", "Same");
        var exception = Assert.Throws<BuildConflictException>(() =>
            new BrandMerger().Merge([TestData.Catalog("aegis", record, record)], MappingSet.Empty));
        Assert.Contains(exception.Conflicts, x => x.Type == "duplicate-source");
    }

    [Fact]
    public void PreservePolicyMarksMissingAssetEvidenceUnknownWithoutClaimingClearance()
    {
        var result = new BrandMerger().Merge(
            [TestData.Catalog("aegis", TestData.Record("aegis", "Example", "Example"))],
            MappingSet.Empty);

        var brand = Assert.Single(result.Document.Brands);
        Assert.Equal("unknown", brand.SelectedSource.Metadata["rightsStatus"]);
        Assert.Equal("no-asset-level-evidence", brand.SelectedSource.Metadata["rightsBasis"]);
        Assert.Equal(1, result.Summary.SelectedRightsByStatus!["unknown"]);
    }

    [Fact]
    public void DocumentedOnlyExcludesUnknownSourcesAndKeepsAssetLevelEvidence()
    {
        var unknown = TestData.Record("aegis", "Example", "Example");
        var documented = TestData.Record("simple-icons", "example", "Example") with
        {
            Metadata = new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["licenseType"] = "CC-BY-4.0",
                ["licenseUrl"] = "https://creativecommons.org/licenses/by/4.0/"
            }
        };
        var mapping = new CanonicalBrandMapping("example", "Example", ["Example"],
            [new("aegis", "Example"), new("simple-icons", "example")]);

        var result = new BrandMerger().Merge(
            [TestData.Catalog("aegis", unknown), TestData.Catalog("simple-icons", documented)],
            TestData.Mappings(mapping), rightsPolicy: RightsPolicy.DocumentedOnly);

        var brand = Assert.Single(result.Document.Brands);
        Assert.Equal("simple-icons", brand.SelectedSource.Provider);
        Assert.Equal("attribution-required", brand.SelectedSource.Metadata["rightsStatus"]);
        Assert.Equal(1, result.Summary.RightsExcludedRecords);
    }

    [Fact]
    public void RequireDocumentedFailsWhenSelectedSourceHasNoAssetEvidence()
    {
        var exception = Assert.Throws<InputValidationException>(() => new BrandMerger().Merge(
            [TestData.Catalog("aegis", TestData.Record("aegis", "Example", "Example"))],
            MappingSet.Empty, rightsPolicy: RightsPolicy.RequireDocumented));

        Assert.Contains("rights status 'unknown'", exception.Message);
    }

    [Fact]
    public void ManualRightsAssessmentRecordsItsEvidenceWithoutCallingItLegalClearance()
    {
        var mappings = new MappingSet([], new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, SourceOverride>(),
            new Dictionary<string, RightsOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["aegis/Example"] = new(RightsStatus.AttributionRequired, "CC-BY-4.0",
                    "https://example.test/evidence", "Reviewed upstream credit record.")
            });

        var result = new BrandMerger().Merge(
            [TestData.Catalog("aegis", TestData.Record("aegis", "Example", "Example"))], mappings,
            rightsPolicy: RightsPolicy.RequireDocumented);

        var brand = Assert.Single(result.Document.Brands);
        Assert.Equal("manual-review", brand.SelectedSource.Metadata["rightsBasis"]);
        Assert.Equal("https://example.test/evidence", brand.SelectedSource.Metadata["rightsEvidenceUrl"]);
    }

    [Fact]
    public void VersionThreeUsesVisibleSimpleIconsBrandColorWithoutAnUnnecessaryTile()
    {
        var record = RecordWithColors("simple-icons", "example", "Example", "#D64027", null);

        var result = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)],
            MappingSet.Empty, formatVersion: 3);

        var appearances = Assert.Single(result.Document.Brands).Appearances!;
        Assert.All(new[] { appearances.LightTheme, appearances.DarkTheme }, appearance =>
        {
            Assert.Equal("complete-source-artwork", appearance.Selection);
            Assert.Equal("complete-icon", appearance.AssetRole);
            Assert.Equal("embedded", appearance.Background.Mode);
            Assert.Null(appearance.Background.Color);
        });
        var canonical = Assert.Single(result.RuntimeVisualReferences!, reference => reference.Appearance == "lightTheme");
        Assert.Contains("fill=\"#D64027\"", System.Text.Encoding.UTF8.GetString(canonical.CanonicalSvg));
    }

    [Fact]
    public void VersionThreeKeepsTileOnlyWhereMonochromeBrandPaintNeedsContrast()
    {
        var record = RecordWithColors("simple-icons", "white-mark", "White Mark", "#FFFFFF", null);

        var result = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)],
            MappingSet.Empty, formatVersion: 3);
        var appearances = Assert.Single(result.Document.Brands).Appearances!;

        Assert.Equal("foreground", appearances.LightTheme.AssetRole);
        Assert.Equal("solid", appearances.LightTheme.Background.Mode);
        Assert.NotEqual("#FFFFFF", appearances.LightTheme.Background.Color);
        Assert.Equal("complete-icon", appearances.DarkTheme.AssetRole);
        Assert.Equal("embedded", appearances.DarkTheme.Background.Mode);
    }

    [Fact]
    public void VersionThreePreservesFullCanvasSourceCompositionAsCompleteIcon()
    {
        var record = RecordWithSvg("aegis", "Example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <rect width="24" height="24" fill="#D64027"/>
              <path d="M6 6h12v12H6z" fill="#FFFFFF"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([TestData.Catalog("aegis", record)],
            MappingSet.Empty, formatVersion: 3);

        var brand = Assert.Single(result.Document.Brands);
        Assert.All(new[] { brand.Appearances!.LightTheme, brand.Appearances.DarkTheme }, appearance =>
        {
            Assert.Equal("complete-icon", appearance.AssetRole);
            Assert.Equal("embedded", appearance.Background.Mode);
            Assert.Null(appearance.Background.Color);
        });
    }

    [Fact]
    public void VersionThreePreservesContainedSourceBackplateAsCompleteIconWithoutAConsumerTile()
    {
        var record = RecordWithSvg("dashboard-icons", "contained", "Contained", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <rect x="1" y="1" width="22" height="22" rx="4" fill="#EC1B24"/>
              <path d="M7 8h10v8H7z" fill="#FFFFFF"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([TestData.Catalog("dashboard-icons", record)],
            MappingSet.Empty, formatVersion: 3);
        var appearances = Assert.Single(result.Document.Brands).Appearances!;

        Assert.All(new[] { appearances.LightTheme, appearances.DarkTheme }, appearance =>
        {
            Assert.Equal("complete-icon", appearance.AssetRole);
            Assert.Equal("embedded", appearance.Background.Mode);
            Assert.Null(appearance.Background.Color);
            Assert.Equal("complete-source-artwork", appearance.Selection);
        });
    }

    [Fact]
    public void VersionThreeCompleteShieldKeepsTransparentExteriorInBothThemes()
    {
        var record = RecordWithSvg("dashboard-icons", "shield-home", "Shield Home", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <path d="M12 1 22 5v6c0 6-4 10-10 12C6 21 2 17 2 11V5z" fill="#67C477"/>
              <path d="M7 11h3v3h4V8h3v9H7z" fill="#FFFFFF"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([TestData.Catalog("dashboard-icons", record)],
            MappingSet.Empty, formatVersion: 3);
        var appearances = Assert.Single(result.Document.Brands).Appearances!;

        foreach (var appearance in new[] { appearances.LightTheme, appearances.DarkTheme })
        {
            Assert.Equal("complete-icon", appearance.AssetRole);
            Assert.Equal("embedded", appearance.Background.Mode);
            using var bitmap = DeterministicPng.DecodeAndValidate(
                result.RuntimeIcons![appearance.Asset.Path], 128, 128, appearance.Asset.Path);
            Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
            Assert.Equal(0, bitmap.GetPixel(127, 0).Alpha);
            Assert.Equal(0, bitmap.GetPixel(0, 127).Alpha);
            Assert.Equal(0, bitmap.GetPixel(127, 127).Alpha);
            Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha == 255
                && pixel.Green > pixel.Red && pixel.Green > pixel.Blue);
            Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha == 255
                && pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245);
        }
    }

    [Fact]
    public void VersionThreeMaterializesAegisImplicitBlackBeforeCompleteIconDetection()
    {
        var record = RecordWithSvg("aegis", "Example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <circle cx="12" cy="12" r="12"/>
              <path d="M8 8h8v8H8z" fill="#FFFFFF"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([TestData.Catalog("aegis", record)],
            MappingSet.Empty, formatVersion: 3);
        var appearance = Assert.Single(result.Document.Brands).Appearances!.DarkTheme;

        Assert.Equal("complete-icon", appearance.AssetRole);
        using var bitmap = DeterministicPng.DecodeAndValidate(
            result.RuntimeIcons![appearance.Asset.Path], 128, 128, appearance.Asset.Path);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 0 && pixel.Red < 10 && pixel.Green < 10 && pixel.Blue < 10);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 0 && pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245);
    }

    [Fact]
    public void VersionTwoPromotesAegisImplicitBlackToRuntimeBackground()
    {
        var record = RecordWithSvg("aegis", "OpenAI", "OpenAI", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <circle cx="12" cy="12" r="12"/>
              <path d="M8 8h8v8H8z" fill="#FFFFFF"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([TestData.Catalog("aegis", record)],
            MappingSet.Empty, formatVersion: 2);
        var brand = Assert.Single(result.Document.Brands);
        var runtime = brand.RuntimeIcon!;

        using var bitmap = DeterministicPng.DecodeAndValidate(
            result.RuntimeIcons![runtime.Path], 128, 128, runtime.Path);
        Assert.Equal("#000000", brand.BackgroundColor);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha > 0 && pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245);
    }

    [Fact]
    public void VersionThreeUsesDashboardVariantsForTheirIntendedApplicationThemes()
    {
        static byte[] Svg(string color) => System.Text.Encoding.UTF8.GetBytes(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M2 2h20v20H2z\" fill=\"{color}\"/></svg>");
        var record = new SourceRecord("dashboard-icons", "example", "Example", ["Example"],
            "svg/example.svg", Svg("#777777"), "default",
            new SortedDictionary<string, string?>(StringComparer.Ordinal), Variants:
            [
                new("example-dark", "svg/example-dark.svg", Svg("#111111"), "light-theme"),
                new("example-light", "svg/example-light.svg", Svg("#EEEEEE"), "dark-theme")
            ]);

        var result = new BrandMerger().Merge([TestData.Catalog("dashboard-icons", record)],
            MappingSet.Empty, formatVersion: 3);
        var appearances = Assert.Single(result.Document.Brands).Appearances!;

        Assert.Equal("example-dark", appearances.LightTheme.Source.SourceId);
        Assert.Equal("example-light", appearances.DarkTheme.Source.SourceId);
        Assert.Equal("native-theme-variant", appearances.LightTheme.Selection);
        Assert.Equal("native-theme-variant", appearances.DarkTheme.Selection);
        Assert.Equal("#FFFFFF", appearances.LightTheme.Background.Color);
        Assert.Equal("#121212", appearances.DarkTheme.Background.Color);
    }

    [Fact]
    public void VersionThreeFallsBackToContrastingDashboardVariantWhenDeclaredThemeVariantIsInvisible()
    {
        static byte[] Svg(string color) => System.Text.Encoding.UTF8.GetBytes(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M2 8h20v8H2z\" fill=\"{color}\"/></svg>");
        var record = new SourceRecord("dashboard-icons", "example", "Example", ["Example"],
            "svg/example.svg", Svg("#777777"), "default",
            new SortedDictionary<string, string?>(StringComparer.Ordinal), Variants:
            [
                // Some pinned Dashboard records have variant metadata whose
                // declared theme is inconsistent with the pixels. Selection
                // must verify the actual artwork rather than trust its name.
                new("example-declared-light", "svg/example-declared-light.svg", Svg("#FFFFFF"), "light-theme"),
                new("example-declared-dark", "svg/example-declared-dark.svg", Svg("#000000"), "dark-theme")
            ]);

        var result = new BrandMerger().Merge([TestData.Catalog("dashboard-icons", record)],
            MappingSet.Empty, formatVersion: 3);
        var appearances = Assert.Single(result.Document.Brands).Appearances!;

        Assert.Equal("example-declared-dark", appearances.LightTheme.Source.SourceId);
        Assert.Equal("example-declared-light", appearances.DarkTheme.Source.SourceId);
        Assert.Equal("native-theme-variant", appearances.LightTheme.Selection);
        Assert.Equal("native-theme-variant", appearances.DarkTheme.Selection);

        using var light = DeterministicPng.DecodeAndValidate(
            result.RuntimeIcons![appearances.LightTheme.Asset.Path], 128, 128,
            appearances.LightTheme.Asset.Path);
        using var dark = DeterministicPng.DecodeAndValidate(
            result.RuntimeIcons[appearances.DarkTheme.Asset.Path], 128, 128,
            appearances.DarkTheme.Asset.Path);
        Assert.Contains(light.Pixels, pixel => pixel.Alpha > 240
            && pixel.Red < 10 && pixel.Green < 10 && pixel.Blue < 10);
        Assert.Contains(dark.Pixels, pixel => pixel.Alpha > 240
            && pixel.Red > 245 && pixel.Green > 245 && pixel.Blue > 245);
    }

    [Fact]
    public void VersionThreeKeepsUnpairedForegroundArtworkOnNeutralLightTileInDarkTheme()
    {
        var record = RecordWithSvg("dashboard-icons", "example", "Example", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <path d="M2 2h20v20H2z" fill="#111111"/>
            </svg>
            """);

        var result = new BrandMerger().Merge([TestData.Catalog("dashboard-icons", record)],
            MappingSet.Empty, formatVersion: 3);
        var appearances = Assert.Single(result.Document.Brands).Appearances!;

        Assert.Equal("foreground", appearances.DarkTheme.AssetRole);
        Assert.Equal("solid", appearances.DarkTheme.Background.Mode);
        Assert.Equal("#FFFFFF", appearances.DarkTheme.Background.Color);
        Assert.Equal(appearances.LightTheme.Asset.Path, appearances.DarkTheme.Asset.Path);
    }

    [Fact]
    public void VersionThreeAppliesOnlyReviewedThemeForegroundColorsWithoutAddingATile()
    {
        var record = new SourceRecord("simple-icons", "example", "Example", ["Example"],
            "icons/example.svg", System.Text.Encoding.UTF8.GetBytes("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                  <path d="M3 8h18v8H3z"/>
                </svg>
                """), "default", new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["nativeBrandColor"] = "#333333"
            });
        var mappings = new MappingSet([], new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, SourceOverride>(StringComparer.Ordinal)
            {
                ["example"] = new("simple-icons", "example", "#333333", "#FFFFFF",
                    "Reviewed synthetic monochrome theme treatment.")
            });

        var result = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)],
            mappings, formatVersion: 3);
        var appearances = Assert.Single(result.Document.Brands).Appearances!;

        Assert.NotEqual(appearances.LightTheme.Asset.Path, appearances.DarkTheme.Asset.Path);
        Assert.All(new[] { appearances.LightTheme, appearances.DarkTheme }, appearance =>
        {
            Assert.Equal("complete-icon", appearance.AssetRole);
            Assert.Equal("embedded", appearance.Background.Mode);
            Assert.Null(appearance.Background.Color);
            Assert.Equal("brand-color-foreground", appearance.Selection);
            Assert.Contains("Reviewed synthetic", appearance.AppearanceSelectionReason);
        });
        using var light = DeterministicPng.DecodeAndValidate(
            result.RuntimeIcons![appearances.LightTheme.Asset.Path], 128, 128,
            appearances.LightTheme.Asset.Path);
        using var dark = DeterministicPng.DecodeAndValidate(
            result.RuntimeIcons[appearances.DarkTheme.Asset.Path], 128, 128,
            appearances.DarkTheme.Asset.Path);
        Assert.Equal(0, light.GetPixel(0, 0).Alpha);
        Assert.Equal(0, dark.GetPixel(0, 0).Alpha);
        Assert.Contains(light.Pixels, pixel => pixel.Alpha == 255
            && pixel.Red == 0x33 && pixel.Green == 0x33 && pixel.Blue == 0x33);
        Assert.Contains(dark.Pixels, pixel => pixel.Alpha == 255
            && pixel.Red == 0xFF && pixel.Green == 0xFF && pixel.Blue == 0xFF);

        var baselineMappings = new MappingSet([], new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, SourceOverride>(StringComparer.Ordinal)
            {
                ["example"] = new("simple-icons", "example")
            });
        var baselineV1 = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)],
            baselineMappings, formatVersion: 1);
        var reviewedV1 = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)],
            mappings, formatVersion: 1);
        Assert.Equal(baselineV1.Icons.Single().Value, reviewedV1.Icons.Single().Value);
        var baselineV2 = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)],
            baselineMappings, formatVersion: 2);
        var reviewedV2 = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)],
            mappings, formatVersion: 2);
        Assert.Equal(baselineV2.RuntimeIcons!.Single().Value, reviewedV2.RuntimeIcons!.Single().Value);
    }

    [Fact]
    public async Task VersionThreeReviewedBadgeCompositionPreservesBrandBadgeWhiteDetailAndTransparentExterior()
    {
        var record = new SourceRecord("simple-icons", "examplebadge", "Example Badge", ["Example Badge"],
            "icons/examplebadge.svg", System.Text.Encoding.UTF8.GetBytes("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                  <path fill-rule="evenodd" d="M6 0h12l6 6v12l-6 6H6l-6-6V6zM8 8h8v8H8z"/>
                </svg>
                """), "default", new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["nativeBrandColor"] = "#C70D2C"
            });
        var composition = new AppearanceCompositionOverride("simple-icons", "examplebadge",
            RuntimeIconRenderer.EnclosedMatteComposition, "#FFFFFF",
            "Reviewed synthetic badge composition.");
        var mappings = new MappingSet([], new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, SourceOverride>(), null,
            new Dictionary<string, AppearanceCompositionOverride>(StringComparer.Ordinal)
            {
                ["example-badge"] = composition
            });

        var result = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)],
            mappings, formatVersion: 3);
        var brand = Assert.Single(result.Document.Brands);
        Assert.All(new[] { brand.Appearances!.LightTheme, brand.Appearances.DarkTheme }, appearance =>
        {
            Assert.Equal("complete-icon", appearance.AssetRole);
            Assert.Equal("embedded", appearance.Background.Mode);
            Assert.Null(appearance.Background.Color);
            Assert.Equal("reviewed-badge-composition", appearance.Selection);
        });
        Assert.Equal(brand.Appearances.LightTheme.Asset.Path, brand.Appearances.DarkTheme.Asset.Path);
        using var bitmap = DeterministicPng.DecodeAndValidate(
            result.RuntimeIcons![brand.Appearances.LightTheme.Asset.Path], 128, 128,
            brand.Appearances.LightTheme.Asset.Path);
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        var center = bitmap.GetPixel(64, 64);
        Assert.Equal(new SkiaSharp.SKColor(255, 255, 255, 255), center);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha == 255
            && pixel.Red == 0xC7 && pixel.Green == 0x0D && pixel.Blue == 0x2C);
        Assert.Contains(bitmap.Pixels, pixel => pixel.Alpha is > 0 and < 255);
        Assert.DoesNotContain(bitmap.Pixels.Take(128 * RuntimeIconRenderer.ThemeContentInset),
            pixel => pixel.Alpha != 0);

        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("reviewed-badge-v3.otphicons");
        await PackSerializer.WriteAsync(path, result);
        var reopened = await PackReader.ReadAndValidateAsync(path);
        PackReader.ValidateV3RuntimeVisuals(reopened, result.RuntimeVisualReferences!);
        var reopenedBrand = Assert.Single(reopened.Document.Brands);
        Assert.All(new[] { reopenedBrand.Appearances!.LightTheme, reopenedBrand.Appearances.DarkTheme }, appearance =>
        {
            Assert.Equal("complete-icon", appearance.AssetRole);
            Assert.Equal("embedded", appearance.Background.Mode);
            Assert.Equal("reviewed-badge-composition", appearance.Selection);
        });
    }

    [Fact]
    public void AppearanceCompositionOverridesDoNotChangeVersionOneOrTwo()
    {
        var record = RecordWithColors("simple-icons", "example", "Example", "#C70D2C", null);
        var composition = new Dictionary<string, AppearanceCompositionOverride>(StringComparer.Ordinal)
        {
            ["example"] = new("simple-icons", "example", RuntimeIconRenderer.EnclosedMatteComposition,
                "#FFFFFF", "Reviewed fixture.")
        };
        var baseline = MappingSet.Empty;
        var reviewed = new MappingSet([], new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<string, SourceOverride>(), null, composition);

        var v1Baseline = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)], baseline, formatVersion: 1);
        var v1Reviewed = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)], reviewed, formatVersion: 1);
        Assert.Equal(v1Baseline.Icons.Single().Value, v1Reviewed.Icons.Single().Value);
        var v2Baseline = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)], baseline, formatVersion: 2);
        var v2Reviewed = new BrandMerger().Merge([TestData.Catalog("simple-icons", record)], reviewed, formatVersion: 2);
        Assert.Equal(v2Baseline.RuntimeIcons!.Single().Value, v2Reviewed.RuntimeIcons!.Single().Value);
    }

    [Fact]
    public void VersionThreeAddsContentSafeInsetWithoutChangingVersionTwoRasterBounds()
    {
        var record = RecordWithSvg("dashboard-icons", "wide", "Wide", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <path d="M0 8h24v8H0z" fill="#123456"/>
            </svg>
            """);

        var v2 = new BrandMerger().Merge([TestData.Catalog("dashboard-icons", record)],
            MappingSet.Empty, formatVersion: 2);
        var v3 = new BrandMerger().Merge([TestData.Catalog("dashboard-icons", record)],
            MappingSet.Empty, formatVersion: 3);
        var v2Runtime = Assert.Single(v2.Document.Brands).RuntimeIcon!;
        var v3Runtime = Assert.Single(v3.Document.Brands).Appearances!.LightTheme.Asset;
        using var v2Bitmap = DeterministicPng.DecodeAndValidate(v2.RuntimeIcons![v2Runtime.Path], 128, 128, v2Runtime.Path);
        using var v3Bitmap = DeterministicPng.DecodeAndValidate(v3.RuntimeIcons![v3Runtime.Path], 128, 128, v3Runtime.Path);

        Assert.Contains(Enumerable.Range(0, 128), y => v2Bitmap.GetPixel(0, y).Alpha != 0);
        Assert.DoesNotContain(Enumerable.Range(0, 128), y => v3Bitmap.GetPixel(0, y).Alpha != 0);
        Assert.Contains(Enumerable.Range(0, 128), y =>
            v3Bitmap.GetPixel(RuntimeIconRenderer.ThemeContentInset, y).Alpha != 0);
    }

    [Fact]
    public async Task VersionThreeSingleWorkerAndParallelBuildsAreByteIdentical()
    {
        var records = Enumerable.Range(0, 12)
            .Select(index => RecordWithSvg("dashboard-icons", $"brand-{index:D2}", $"Brand {index:D2}", $"""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
                  <path d="M2 2h20v20H2z" fill="#{index + 32:X2}6699"/>
                </svg>
                """))
            .ToArray();
        var catalog = TestData.Catalog("dashboard-icons", records);
        var single = new BrandMerger().Merge([catalog], MappingSet.Empty,
            formatVersion: 3, maxDegreeOfParallelism: 1);
        var parallel = new BrandMerger().Merge([catalog], MappingSet.Empty,
            formatVersion: 3, maxDegreeOfParallelism: 4);
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var singlePath = fixture.FilePath("single.otphicons");
        var parallelPath = fixture.FilePath("parallel.otphicons");

        await PackSerializer.WriteAsync(singlePath, single);
        await PackSerializer.WriteAsync(parallelPath, parallel);

        Assert.Equal(await File.ReadAllBytesAsync(singlePath), await File.ReadAllBytesAsync(parallelPath));
    }

    private static SourceRecord RecordWithColors(
        string provider,
        string sourceId,
        string displayName,
        string? native,
        string? extracted)
        => new(provider, sourceId, displayName, [displayName], provider + "/" + sourceId + ".svg",
            System.Text.Encoding.UTF8.GetBytes(TestData.Svg), provider == "aegis" ? "primary" : "default",
            new SortedDictionary<string, string?>(StringComparer.Ordinal)
            {
                ["extractedBrandColor"] = extracted,
                ["nativeBrandColor"] = native
            });

    private static SourceRecord RecordWithSvg(string provider, string sourceId, string displayName, string svg)
        => new(provider, sourceId, displayName, [displayName], provider + "/" + sourceId + ".svg",
            System.Text.Encoding.UTF8.GetBytes(svg), provider == "aegis" ? "primary" : "default",
            new SortedDictionary<string, string?>(StringComparer.Ordinal));
}
