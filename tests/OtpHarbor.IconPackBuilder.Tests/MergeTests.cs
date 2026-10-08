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
    public void DefaultPreferenceIsAegisThenDashboardThenSimpleIcons()
    {
        var mapping = new CanonicalBrandMapping("github", "GitHub", ["GitHub"],
            [new("aegis", "GitHub"), new("dashboard-icons", "github"), new("simple-icons", "github")]);
        var result = new BrandMerger().Merge([
            TestData.Catalog("simple-icons", TestData.Record("simple-icons", "github", "GitHub")),
            TestData.Catalog("dashboard-icons", TestData.Record("dashboard-icons", "github", "GitHub")),
            TestData.Catalog("aegis", TestData.Record("aegis", "GitHub", "GitHub"))], TestData.Mappings(mapping));
        Assert.Equal("aegis", Assert.Single(result.Document.Brands).SelectedSource.Provider);
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
    public void NeutralTwoColorCandidateDoesNotBeatSourceBackedBadge()
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
        Assert.Equal("aegis", selected.SelectedSource.Provider);
        Assert.Equal("#E1251B", selected.BackgroundColor);
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
