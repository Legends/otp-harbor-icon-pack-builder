using OtpHarbor.IconPackBuilder.Normalization;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class MappingTests
{
    [Fact]
    public async Task DefaultMappingsAreEmbeddedForStandaloneDistribution()
    {
        var mappings = await MappingLoader.LoadDefaultAsync();

        Assert.NotEmpty(mappings.CanonicalBrands);
        Assert.NotEmpty(mappings.SourceOverrides);
        Assert.NotNull(mappings.EffectiveRightsOverrides);
        Assert.Equal(5, mappings.EffectiveAppearanceCompositions.Count);
        Assert.Equal(RuntimeIconRenderer.EnclosedMatteComposition,
            mappings.EffectiveAppearanceCompositions["adaway"].Mode);
        Assert.Equal("2k", mappings.EffectiveAppearanceCompositions["2k"].SourceId);
        Assert.Equal("dotenv", mappings.EffectiveAppearanceCompositions["env"].SourceId);
        Assert.Equal(["Outlook", "MS Outlook", "Outlook 365"],
            mappings.IssuerAliases["microsoft-outlook"]);
        Assert.Contains("Word", mappings.IssuerAliases["microsoft-word"]);
        Assert.Contains("Excel 365", mappings.IssuerAliases["microsoft-excel"]);
        Assert.Contains("PowerPoint", mappings.IssuerAliases["microsoft-powerpoint"]);
    }

    [Fact]
    public async Task MicrosoftProductAliasesAreEmittedAsExactPackLookupKeys()
    {
        static SourceRecord Record(string sourceId, string name) => new(
            "dashboard-icons", sourceId, name, [name], $"svg/{sourceId}.svg",
            System.Text.Encoding.UTF8.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M2 2h20v20H2z\" fill=\"#123456\"/></svg>"),
            "default", new SortedDictionary<string, string?>(StringComparer.Ordinal));

        var mappings = await MappingLoader.LoadDefaultAsync();
        var result = new BrandMerger().Merge([
            TestData.Catalog("dashboard-icons",
                Record("microsoft-outlook", "Microsoft Outlook"),
                Record("microsoft-word", "Microsoft Word"),
                Record("microsoft-excel", "Microsoft Excel"),
                Record("microsoft-powerpoint", "Microsoft PowerPoint"))
        ], mappings, formatVersion: 1);
        var aliases = result.Document.IssuerAliases.ToDictionary(x => x.Key, x => x.BrandId,
            StringComparer.Ordinal);

        Assert.Equal("microsoft-outlook", aliases[IssuerNormalizer.Normalize("Outlook")]);
        Assert.Equal("microsoft-outlook", aliases[IssuerNormalizer.Normalize("MS Outlook")]);
        Assert.Equal("microsoft-outlook", aliases[IssuerNormalizer.Normalize("Outlook 365")]);
        Assert.Equal("microsoft-word", aliases[IssuerNormalizer.Normalize("Word")]);
        Assert.Equal("microsoft-excel", aliases[IssuerNormalizer.Normalize("Excel 365")]);
        Assert.Equal("microsoft-powerpoint", aliases[IssuerNormalizer.Normalize("MS PowerPoint")]);
    }

    [Fact]
    public async Task SeedMappingsContainRequiredDistinctBrands()
    {
        var mappings = await MappingLoader.LoadAsync(FindRepositoryDirectory("mappings", "canonical-brands.json"));
        var ids = mappings.CanonicalBrands.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("amazon", ids);
        Assert.Contains("amazon-web-services", ids);
        Assert.Contains("microsoft", ids);
        Assert.Contains("microsoft-365", ids);
        Assert.Contains("google", ids);
        Assert.Contains("github", ids);
        Assert.Contains("bosch", ids);
        Assert.Contains("siemens", ids);
        Assert.Contains("zabka", ids);

        var chrome = Assert.Single(mappings.CanonicalBrands, brand => brand.Id == "google-chrome");
        Assert.Contains("Chrome", chrome.Aliases);
        Assert.Contains(chrome.Matches, match => match.Provider == "dashboard-icons" && match.SourceId == "chrome");
        Assert.Contains(chrome.Matches, match => match.Provider == "dashboard-icons" && match.SourceId == "google-chrome");

        var edge = Assert.Single(mappings.CanonicalBrands, brand => brand.Id == "microsoft-edge");
        Assert.Contains("Edge", edge.Aliases);
        Assert.Equal(new SourceOverride("dashboard-icons", "microsoft-edge"),
            mappings.SourceOverrides["microsoft-edge"]);

        var bitcoin = Assert.Single(mappings.CanonicalBrands, brand => brand.Id == "bitcoin");
        Assert.Equal("#FFFFFF", bitcoin.BackgroundColor);

        var sevenZip = Assert.Single(mappings.CanonicalBrands, brand => brand.Id == "7zip");
        Assert.Equal("#FFFFFF", sevenZip.BackgroundColor);
        Assert.Equal(new SourceOverride("simple-icons", "7zip"), mappings.SourceOverrides["7zip"]);
        Assert.Equal("#FFFFFF", mappings.SourceOverrides["about-me"].DarkForegroundColor);
        Assert.Equal("#333333", mappings.SourceOverrides["about-me"].LightForegroundColor);
        Assert.Equal("#FFFFFF", mappings.SourceOverrides["abuse-ch"].DarkForegroundColor);
        Assert.Equal("#00465B", mappings.SourceOverrides["abuse-ch"].LightForegroundColor);

        var openAi = Assert.Single(mappings.CanonicalBrands, brand => brand.Id == "openai");
        Assert.Equal("#FFFFFF", openAi.BackgroundColor);

        var sony = Assert.Single(mappings.CanonicalBrands, brand => brand.Id == "sony");
        Assert.Equal("#FFFFFF", sony.BackgroundColor);
        Assert.Equal(new SourceOverride("simple-icons", "sony"), mappings.SourceOverrides["sony"]);
    }

    [Fact]
    public async Task CanonicalMappingLoadsAndValidatesExplicitBackgroundColor()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var directory = Path.Combine(fixture.DirectoryPath, "mappings");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "canonical-brands.json"),
            "{\"version\":1,\"brands\":[{\"id\":\"github\",\"displayName\":\"GitHub\",\"backgroundColor\":\"#aabbcc\",\"aliases\":[],\"matches\":[]}]}");
        await File.WriteAllTextAsync(Path.Combine(directory, "issuer-aliases.json"), "{\"version\":1,\"aliases\":{}}");
        await File.WriteAllTextAsync(Path.Combine(directory, "source-overrides.json"), "{\"version\":1,\"overrides\":{}}");

        var mappings = await MappingLoader.LoadAsync(directory);

        Assert.Equal("#AABBCC", Assert.Single(mappings.CanonicalBrands).BackgroundColor);
    }

    [Fact]
    public async Task ThemeForegroundOverrideRequiresCanonicalColorAndRationale()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var directory = Path.Combine(fixture.DirectoryPath, "mappings");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "canonical-brands.json"),
            "{\"version\":1,\"brands\":[]}");
        await File.WriteAllTextAsync(Path.Combine(directory, "issuer-aliases.json"),
            "{\"version\":1,\"aliases\":{}}");
        await File.WriteAllTextAsync(Path.Combine(directory, "source-overrides.json"),
            "{\"version\":1,\"overrides\":{\"example\":{\"provider\":\"simple-icons\",\"sourceId\":\"example\",\"darkForegroundColor\":\"white\"}}}");

        var exception = await Assert.ThrowsAsync<InputValidationException>(() => MappingLoader.LoadAsync(directory));

        Assert.Contains("invalid darkForegroundColor", exception.Message);
    }

    [Fact]
    public async Task RightsAssessmentsRequireRecognizedStatusAndHttpsEvidence()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var directory = Path.Combine(fixture.DirectoryPath, "mappings");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "canonical-brands.json"), "{\"version\":1,\"brands\":[]}");
        await File.WriteAllTextAsync(Path.Combine(directory, "issuer-aliases.json"), "{\"version\":1,\"aliases\":{}}");
        await File.WriteAllTextAsync(Path.Combine(directory, "source-overrides.json"), "{\"version\":1,\"overrides\":{}}");
        await File.WriteAllTextAsync(Path.Combine(directory, "rights-assessments.json"),
            "{\"version\":1,\"assessments\":{\"aegis/Example\":{\"status\":\"attribution-required\",\"licenseType\":\"CC-BY-4.0\",\"evidenceUrl\":\"https://example.test/evidence\"}}}");

        var mappings = await MappingLoader.LoadAsync(directory);

        var assessment = Assert.Single(mappings.EffectiveRightsOverrides).Value;
        Assert.Equal(RightsStatus.AttributionRequired, assessment.Status);
        Assert.Equal("CC-BY-4.0", assessment.LicenseType);
    }

    internal static string FindRepositoryDirectory(string child, string expectedFile)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, child);
            if (File.Exists(Path.Combine(candidate, expectedFile))) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(child);
    }
}
