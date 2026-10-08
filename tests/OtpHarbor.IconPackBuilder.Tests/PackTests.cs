using System.IO.Compression;
using System.Text.Json;
using OtpHarbor.IconPackBuilder.Packaging;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class PackTests
{
    [Fact]
    public async Task PackWriteIsByteDeterministicAndEntriesAreOrdered()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var result = BuildResult();
        var first = fixture.FilePath("first.otphicons");
        var second = fixture.FilePath("second.otphicons");

        await PackSerializer.WriteAsync(first, result);
        await PackSerializer.WriteAsync(second, result);

        Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
        using var file = File.OpenRead(first);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.Equal(["pack.json", "icons/", "icons/github.svg", "licenses/"], zip.Entries.Select(x => x.FullName));
        Assert.All(zip.Entries, x => Assert.Equal(1980, x.LastWriteTime.Year));
    }

    [Fact]
    public async Task PackReadWriteRoundTripValidatesSelectedSourcesAndAssets()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("roundtrip.otphicons");
        await PackSerializer.WriteAsync(path, BuildResult());

        var read = await PackReader.ReadAndValidateAsync(path);

        Assert.Equal("otp-harbor-icons", read.Document.PackId);
        Assert.Equal("github", Assert.Single(read.Document.Brands).Id);
        Assert.Equal("#334155", Assert.Single(read.Document.Brands).BackgroundColor);
        Assert.Contains("icons/github.svg", read.Files.Keys);
    }

    [Fact]
    public async Task PackJsonConformsToVersionedSchemaContract()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("schema.otphicons");
        await PackSerializer.WriteAsync(path, BuildResult());
        var schemaPath = Path.Combine(
            MappingTests.FindRepositoryDirectory("schemas", "otp-harbor-icon-pack.schema.json"),
            "otp-harbor-icon-pack.schema.json");
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(schemaPath));
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(1, schema.RootElement.GetProperty("properties").GetProperty("formatVersion").GetProperty("const").GetInt32());

        using var file = File.OpenRead(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        using var pack = JsonDocument.Parse(zip.GetEntry("pack.json")!.Open());
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.All(required, property => Assert.True(pack.RootElement.TryGetProperty(property, out _), property));
        Assert.Equal(1, pack.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$", pack.RootElement.GetProperty("brands")[0].GetProperty("id").GetString()!);
        Assert.Matches("^#[0-9A-F]{6}$", pack.RootElement.GetProperty("brands")[0].GetProperty("backgroundColor").GetString()!);
    }

    [Fact]
    public async Task ReaderRejectsPackWithMissingIcon()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("bad.otphicons");
        var result = BuildResult();
        var missing = result with { Icons = new Dictionary<string, byte[]>() };
        await PackSerializer.WriteAsync(path, missing);
        var exception = await Assert.ThrowsAsync<InputValidationException>(() => PackReader.ReadAndValidateAsync(path));
        Assert.Contains("missing icon", exception.Message);
    }

    [Fact]
    public async Task ReaderRevalidatesSvgContentAfterWriting()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("unsafe.otphicons");
        var result = BuildResult();
        var unsafeIcons = result.Icons.ToDictionary(x => x.Key, _ =>
            System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>"));
        await PackSerializer.WriteAsync(path, result with { Icons = unsafeIcons });

        var exception = await Assert.ThrowsAsync<InputValidationException>(() => PackReader.ReadAndValidateAsync(path));

        Assert.Contains("unsafe SVG", exception.Message);
    }

    [Fact]
    public async Task ReaderRejectsUnreferencedFiles()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("extra.otphicons");
        var result = BuildResult();
        var licenses = result.Licenses.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        licenses["licenses/unreferenced.txt"] = [1, 2, 3];
        await PackSerializer.WriteAsync(path, result with { Licenses = licenses });

        var exception = await Assert.ThrowsAsync<InputValidationException>(() => PackReader.ReadAndValidateAsync(path));

        Assert.Contains("unexpected or unreferenced", exception.Message);
    }

    private static PackBuildResult BuildResult()
        => new BrandMerger().Merge([TestData.Catalog("aegis", TestData.Record("aegis", "GitHub", "GitHub", "github.com"))], MappingSet.Empty);
}
