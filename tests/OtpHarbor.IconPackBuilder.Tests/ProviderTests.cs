using OtpHarbor.IconPackBuilder.Providers;
using OtpHarbor.IconPackBuilder.Providers.Aegis;
using OtpHarbor.IconPackBuilder.Providers.DashboardIcons;
using OtpHarbor.IconPackBuilder.Providers.SimpleIcons;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class ProviderTests
{
    [Fact]
    public async Task AegisReadsPrimaryAliasesAndSkipsVariationsAndGenericIcons()
    {
        const string pack = """
        { "uuid":"c553f06f-2a17-46ca-87f5-56af90dd0500", "name":"fixture", "version":2, "icons":[
          {"name":"GitHub","filename":"icons/1_Primary/GitHub.svg","category":null,"issuer":["github.com","Git Hub"]},
          {"name":"GitHub alt","filename":"icons/2_Variations/GitHub alt.svg","issuer":["alt"]},
          {"name":"Banking","filename":"icons/3_Categories/Banking.svg","issuer":["bank"]},
          {"name":"Privacy","filename":"icons/3_Generic/Privacy.svg","issuer":["privacy"]}
        ]}
        """;
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["aegis-icons-main/pack.json"] = pack,
            ["aegis-icons-main/icons/1_Primary/GitHub.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle fill=\"#aabbcc\"/><path d=\"M0 0h1v1z\"/></svg>",
            ["aegis-icons-main/icons/2_Variations/GitHub alt.svg"] = TestData.Svg,
            ["aegis-icons-main/icons/3_Categories/Banking.svg"] = TestData.Svg,
            ["aegis-icons-main/icons/3_Generic/Privacy.svg"] = TestData.Svg,
            ["aegis-icons-main/LICENSE.md"] = "fixture license",
            ["aegis-icons-main/README.md"] = "fixture credits"
        });

        var catalog = await new AegisProvider().LoadAsync(new("aegis", archive.Path));

        Assert.Equal(2, catalog.Records.Count);
        var record = Assert.Single(catalog.Records, x => x.AutomaticCandidate);
        Assert.Equal("GitHub", record.SourceId);
        Assert.Contains("github.com", record.Aliases);
        Assert.Equal("primary", record.Quality);
        Assert.Equal("#AABBCC", record.Metadata["extractedBrandColor"]);
        var variation = Assert.Single(catalog.Records, x => !x.AutomaticCandidate);
        Assert.Equal("variation", variation.Quality);
        Assert.Equal(2, catalog.SkippedRecords);
        Assert.Equal(["LICENSE.md", "README.md"], catalog.Licenses.Select(x => x.FileName));
    }

    [Fact]
    public async Task SimpleIconsReadsCurrentMetadataAndSvg()
    {
        const string metadata = """
        [{"title":"Amazon Web Services","slug":"amazonwebservices","hex":"FF9900",
          "source":"https://aws.amazon.com/","guidelines":"https://aws.amazon.com/brand/",
          "license":{"type":"Apache-2.0","url":"https://example.test/license"},
          "aliases":{"aka":["AWS"],"old":["Amazon AWS"],"loc":{"de-DE":"Amazon Webdienste"}}}]
        """;
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["simple-icons-1/data/simple-icons.json"] = metadata,
            ["simple-icons-1/icons/amazonwebservices.svg"] = TestData.Svg,
            ["simple-icons-1/package.json"] = "{\"version\":\"15.0.0\"}",
            ["simple-icons-1/node_modules/example/package.json"] = "{\"version\":\"999.0.0\"}"
        });

        var catalog = await new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path));

        var record = Assert.Single(catalog.Records);
        Assert.Equal("Amazon Web Services", record.DisplayName);
        Assert.Contains("AWS", record.Aliases);
        Assert.Contains("Amazon Webdienste", record.Aliases);
        Assert.Equal("#FF9900", record.Metadata["nativeBrandColor"]);
        Assert.Equal("Apache-2.0", record.Metadata["licenseType"]);
        Assert.Equal("15.0.0", catalog.Metadata["version"]);
    }

    [Theory]
    [InlineData(".NET", "dotnet")]
    [InlineData("C++", "cplusplus")]
    [InlineData("1&1", "1and1")]
    [InlineData("Pop!_OS", "popos")]
    public async Task SimpleIconsUsesProviderCompatibleTitleToSlugRules(string title, string slug)
    {
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["data/simple-icons.json"] = $"[{{\"title\":\"{title}\",\"hex\":\"123456\",\"source\":\"https://example.test\"}}]",
            [$"icons/{slug}.svg"] = TestData.Svg
        });

        var record = Assert.Single((await new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path))).Records);

        Assert.Equal(slug, record.SourceId);
    }

    [Fact]
    public async Task DashboardReadsDefaultSvgAndVariantMetadata()
    {
        const string metadata = """
        {"nextcloud":{"base":"svg","aliases":["Next Cloud"],"categories":["cloud","storage"],"source":"https://nextcloud.com","license":"AGPL-3.0",
          "update":{"timestamp":"2026-01-01T00:00:00Z","author":{"login":"fixture-author"}},
          "colors":{"light":"nextcloud-light","dark":"nextcloud-dark"}}}
        """;
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["dashboard-icons-main/metadata.json"] = metadata,
            ["dashboard-icons-main/svg/nextcloud.svg"] = TestData.Svg,
            ["dashboard-icons-main/svg/nextcloud-light.svg"] = TestData.Svg,
            ["dashboard-icons-main/svg/nextcloud-dark.svg"] = TestData.Svg,
            ["dashboard-icons-main/LICENSE"] = "Apache License 2.0 fixture"
        });

        var revision = new string('a', 40);
        var catalog = await new DashboardIconsProvider().LoadAsync(
            new("dashboard-icons", archive.Path, Revision: revision));
        var record = Assert.Single(catalog.Records);

        Assert.Equal("Nextcloud", record.DisplayName);
        Assert.Equal("default", record.Quality);
        Assert.Equal("nextcloud-light", record.Metadata["lightVariant"]);
        Assert.Equal("nextcloud-dark", record.Metadata["darkVariant"]);
        Assert.Equal("fixture-author", record.Metadata["author"]);
        Assert.Equal("cloud,storage", record.Metadata["categories"]);
        Assert.Equal("Apache-2.0", catalog.Metadata["licenseType"]);
        Assert.Equal($"https://github.com/homarr-labs/dashboard-icons/blob/{revision}/LICENSE", catalog.Metadata["licenseUrl"]);
        Assert.Equal("LICENSE", Assert.Single(catalog.Licenses).FileName);
    }

    [Fact]
    public async Task MalformedMetadataFailsWithProviderDiagnostic()
    {
        using var archive = new TestArchive(new Dictionary<string, string> { ["data/simple-icons.json"] = "[{" });
        var exception = await Assert.ThrowsAsync<InputValidationException>(() =>
            new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path)));
        Assert.Contains("simple-icons", exception.Message);
        Assert.Contains("malformed JSON", exception.Message);
    }

    [Fact]
    public async Task MissingIconAssetFailsActionably()
    {
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["data/simple-icons.json"] = "[{\"title\":\"Missing\",\"hex\":\"000000\",\"source\":\"https://example.test\"}]"
        });
        var exception = await Assert.ThrowsAsync<InputValidationException>(() =>
            new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path)));
        Assert.Contains("Missing referenced icon", exception.Message);
        Assert.Contains("missing.svg", exception.Message);
    }

    [Fact]
    public async Task MalformedSvgFailsWithoutRendering()
    {
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["data/simple-icons.json"] = "[{\"title\":\"Bad\",\"hex\":\"123456\",\"source\":\"https://example.test\"}]",
            ["icons/bad.svg"] = "<svg><path></svg>"
        });
        var exception = await Assert.ThrowsAsync<InputValidationException>(() =>
            new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path)));
        Assert.Contains("malformed or unsafe SVG", exception.Message);
    }

    [Fact]
    public async Task SvgWithExternalReferenceIsRejectedLikeTheApplicationImporter()
    {
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["data/simple-icons.json"] = "[{\"title\":\"Unsafe\",\"hex\":\"123456\",\"source\":\"https://example.test\"}]",
            ["icons/unsafe.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0\"/><image href=\"https://example.test/x.png\"/></svg>"
        });

        var exception = await Assert.ThrowsAsync<InputValidationException>(() =>
            new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path)));

        Assert.Contains("externally referenced", exception.Message);
    }

    [Fact]
    public async Task SvgWithResolvedLocalReferencesIsAcceptedLikeTheApplicationImporter()
    {
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["data/simple-icons.json"] = "[{\"title\":\"Local Reference\",\"hex\":\"123456\",\"source\":\"https://example.test\"}]",
            ["icons/localreference.svg"] = """
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">
                  <defs><path id="shape" d="M0 0h1v1z"/></defs>
                  <use xlink:href="#shape"/>
                </svg>
                """
        });

        var catalog = await new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path));

        Assert.Equal("localreference", Assert.Single(catalog.Records).SourceId);
    }

    [Fact]
    public async Task SvgWithUnresolvedLocalReferenceIsRejectedLikeTheApplicationImporter()
    {
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["data/simple-icons.json"] = "[{\"title\":\"Broken Reference\",\"hex\":\"123456\",\"source\":\"https://example.test\"}]",
            ["icons/brokenreference.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0h1v1z\"/><use href=\"#missing\"/></svg>"
        });

        var exception = await Assert.ThrowsAsync<InputValidationException>(() =>
            new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path)));

        Assert.Contains("unresolved local reference", exception.Message);
    }

    [Fact]
    public async Task SvgWithDuplicateIdentifiersIsRejectedLikeTheApplicationImporter()
    {
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["data/simple-icons.json"] = "[{\"title\":\"Duplicate Id\",\"hex\":\"123456\",\"source\":\"https://example.test\"}]",
            ["icons/duplicateid.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><path id=\"shape\" d=\"M0 0h1v1z\"/><path id=\"shape\" d=\"M1 1h1v1z\"/></svg>"
        });

        var exception = await Assert.ThrowsAsync<InputValidationException>(() =>
            new SimpleIconsProvider().LoadAsync(new("simple-icons", archive.Path)));

        Assert.Contains("duplicate identifiers", exception.Message);
    }
}
