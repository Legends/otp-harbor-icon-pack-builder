namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class MappingTests
{
    [Fact]
    public async Task SeedMappingsContainRequiredDistinctBrands()
    {
        var mappings = await MappingLoader.LoadAsync(FindRepositoryDirectory("mappings"));
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

    internal static string FindRepositoryDirectory(string child)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, child);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(child);
    }
}
