using System.IO.Compression;
using System.Text.Json;
using OtpHarbor.IconPackBuilder.Normalization;
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
    public async Task VersionTwoPackIsByteDeterministicAndHasCanonicalRuntimeAssets()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var result = BuildResult(2);
        var first = fixture.FilePath("first-v2.otphicons");
        var second = fixture.FilePath("second-v2.otphicons");

        await PackSerializer.WriteAsync(first, result);
        await PackSerializer.WriteAsync(second, result);

        Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
        var read = await PackReader.ReadAndValidateAsync(first);
        var brand = Assert.Single(read.Document.Brands);
        Assert.Equal(2, read.Document.FormatVersion);
        Assert.Equal("runtime/foreground-128/github.png", brand.RuntimeIcon!.Path);
        Assert.Equal(DeterministicPng.Sha256(read.Files[brand.RuntimeIcon.Path]), brand.RuntimeIcon.Sha256);
        using var file = File.OpenRead(first);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.Equal([
            "pack.json", "icons/", "icons/github.svg", "runtime/", "runtime/foreground-128/",
            "runtime/foreground-128/github.png", "licenses/"
        ], zip.Entries.Select(x => x.FullName));
        Assert.All(zip.Entries, entry => Assert.Equal(1980, entry.LastWriteTime.Year));
    }

    [Fact]
    public async Task VersionTwoManifestConformsToSeparateSchemaContract()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("schema-v2.otphicons");
        await PackSerializer.WriteAsync(path, BuildResult(2));
        var schemaPath = Path.Combine(
            MappingTests.FindRepositoryDirectory("schemas", "otp-harbor-icon-pack-v2.schema.json"),
            "otp-harbor-icon-pack-v2.schema.json");
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(schemaPath));
        Assert.Equal(2, schema.RootElement.GetProperty("properties").GetProperty("formatVersion").GetProperty("const").GetInt32());
        using var file = File.OpenRead(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        using var pack = JsonDocument.Parse(zip.GetEntry("pack.json")!.Open());
        var runtime = pack.RootElement.GetProperty("brands")[0].GetProperty("runtimeIcon");
        Assert.Equal(128, runtime.GetProperty("width").GetInt32());
        Assert.Equal("otp-harbor-foreground-v1", runtime.GetProperty("renderProfile").GetString());
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

    [Fact]
    public async Task VersionTwoReaderRejectsMissingRuntimeAsset()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("missing-runtime.otphicons");
        var result = BuildResult(2) with { RuntimeIcons = new Dictionary<string, byte[]>() };
        await PackSerializer.WriteAsync(path, result);

        var exception = await Assert.ThrowsAsync<InputValidationException>(() => PackReader.ReadAndValidateAsync(path));

        Assert.Contains("runtime icon", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VersionTwoReaderRejectsMissingRuntimeMetadata()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("missing-runtime-metadata.otphicons");
        var result = BuildResult(2);
        var brand = Assert.Single(result.Document.Brands);
        await PackSerializer.WriteAsync(path, result with
        {
            Document = CopyDocument(result.Document, brand with { RuntimeIcon = null })
        });

        var exception = await Assert.ThrowsAsync<InputValidationException>(() => PackReader.ReadAndValidateAsync(path));

        Assert.Contains("no runtime icon", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VersionTwoReaderRejectsWrongHashAndDimensions()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var result = BuildResult(2);
        var brand = Assert.Single(result.Document.Brands);
        var wrongHash = result with
        {
            Document = CopyDocument(result.Document, brand with
            {
                RuntimeIcon = brand.RuntimeIcon! with { Sha256 = new string('0', 64) }
            })
        };
        var hashPath = fixture.FilePath("wrong-hash.otphicons");
        await PackSerializer.WriteAsync(hashPath, wrongHash);
        Assert.Contains("SHA-256", (await Assert.ThrowsAsync<InputValidationException>(
            () => PackReader.ReadAndValidateAsync(hashPath))).Message);

        var wrongDimensions = result with
        {
            Document = CopyDocument(result.Document, brand with
            {
                RuntimeIcon = brand.RuntimeIcon! with { Width = 64 }
            })
        };
        var dimensionsPath = fixture.FilePath("wrong-dimensions.otphicons");
        await PackSerializer.WriteAsync(dimensionsPath, wrongDimensions);
        Assert.Contains("runtime icon", (await Assert.ThrowsAsync<InputValidationException>(
            () => PackReader.ReadAndValidateAsync(dimensionsPath))).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VersionTwoReaderRejectsPngWhoseDecodedDimensionsAreWrong()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var result = BuildResult(2);
        var brand = Assert.Single(result.Document.Brands);
        var wrongPng = DeterministicPng.EncodeRgba(64, 128, new byte[64 * 128 * 4]);
        var runtime = new Dictionary<string, byte[]>
        {
            [brand.RuntimeIcon!.Path] = wrongPng
        };
        var altered = result with
        {
            RuntimeIcons = runtime,
            Document = CopyDocument(result.Document, brand with
            {
                RuntimeIcon = brand.RuntimeIcon with { Sha256 = DeterministicPng.Sha256(wrongPng) }
            })
        };
        var path = fixture.FilePath("wrong-decoded-size.otphicons");
        await PackSerializer.WriteAsync(path, altered);

        var exception = await Assert.ThrowsAsync<InputValidationException>(() => PackReader.ReadAndValidateAsync(path));

        Assert.Contains("IHDR", exception.Message);
    }

    [Fact]
    public async Task VersionTwoReaderRejectsCorruptAndUnreferencedRuntimePngs()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var result = BuildResult(2);
        var runtime = result.RuntimeIcons!.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal);
        runtime.Single().Value[0] ^= 0xff;
        var brand = Assert.Single(result.Document.Brands);
        var corrupt = result with
        {
            RuntimeIcons = runtime,
            Document = CopyDocument(result.Document, brand with
            {
                RuntimeIcon = brand.RuntimeIcon! with { Sha256 = DeterministicPng.Sha256(runtime.Single().Value) }
            })
        };
        var corruptPath = fixture.FilePath("corrupt-runtime.otphicons");
        await PackSerializer.WriteAsync(corruptPath, corrupt);
        Assert.Contains("signature", (await Assert.ThrowsAsync<InputValidationException>(
            () => PackReader.ReadAndValidateAsync(corruptPath))).Message, StringComparison.OrdinalIgnoreCase);

        runtime = result.RuntimeIcons!.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        runtime["runtime/foreground-128/unreferenced.png"] = runtime.Single().Value;
        var extraPath = fixture.FilePath("extra-runtime.otphicons");
        await PackSerializer.WriteAsync(extraPath, result with { RuntimeIcons = runtime });
        Assert.Contains("unexpected or unreferenced", (await Assert.ThrowsAsync<InputValidationException>(
            () => PackReader.ReadAndValidateAsync(extraPath))).Message);
    }

    [Fact]
    public async Task VersionOneNeverContainsRuntimeMetadataOrFiles()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var path = fixture.FilePath("frozen-v1.otphicons");
        await PackSerializer.WriteAsync(path, BuildResult());
        using var file = File.OpenRead(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.StartsWith("runtime/", StringComparison.Ordinal));
        using var manifest = JsonDocument.Parse(zip.GetEntry("pack.json")!.Open());
        Assert.False(manifest.RootElement.GetProperty("brands")[0].TryGetProperty("runtimeIcon", out _));
        await PackReader.ReadAndValidateAsync(path);
    }

    [Fact]
    public async Task PackReaderRejectsUnknownManifestMembers()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var original = fixture.FilePath("original.otphicons");
        var altered = fixture.FilePath("unknown-member.otphicons");
        await PackSerializer.WriteAsync(original, BuildResult());
        using (var inputFile = File.OpenRead(original))
        using (var input = new ZipArchive(inputFile, ZipArchiveMode.Read))
        await using (var outputFile = File.Create(altered))
        using (var output = new ZipArchive(outputFile, ZipArchiveMode.Create))
        {
            foreach (var entry in input.Entries)
            {
                var created = output.CreateEntry(entry.FullName);
                if (entry.Name.Length == 0) continue;
                await using var destination = created.Open();
                if (entry.FullName == "pack.json")
                {
                    using var document = JsonDocument.Parse(entry.Open());
                    using var buffer = new MemoryStream();
                    await using (var writer = new Utf8JsonWriter(buffer))
                    {
                        writer.WriteStartObject();
                        foreach (var property in document.RootElement.EnumerateObject()) property.WriteTo(writer);
                        writer.WriteBoolean("unknownMember", true);
                        writer.WriteEndObject();
                    }
                    buffer.Position = 0;
                    await buffer.CopyToAsync(destination);
                }
                else
                {
                    await using var source = entry.Open();
                    await source.CopyToAsync(destination);
                }
            }
        }

        Assert.Contains("Malformed pack.json", (await Assert.ThrowsAsync<InputValidationException>(
            () => PackReader.ReadAndValidateAsync(altered))).Message);
    }

    [Fact]
    public async Task PackReaderRequiresManifestAtCanonicalRootPath()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var original = fixture.FilePath("rooted.otphicons");
        var nested = fixture.FilePath("nested.otphicons");
        await PackSerializer.WriteAsync(original, BuildResult());
        using (var inputFile = File.OpenRead(original))
        using (var input = new ZipArchive(inputFile, ZipArchiveMode.Read))
        await using (var outputFile = File.Create(nested))
        using (var output = new ZipArchive(outputFile, ZipArchiveMode.Create))
        {
            foreach (var entry in input.Entries.Where(value => value.Name.Length > 0))
            {
                var path = entry.FullName == "pack.json" ? "nested/pack.json" : entry.FullName;
                var created = output.CreateEntry(path);
                await using var source = entry.Open();
                await using var destination = created.Open();
                await source.CopyToAsync(destination);
            }
        }

        Assert.Contains("canonical root path", (await Assert.ThrowsAsync<InputValidationException>(
            () => PackReader.ReadAndValidateAsync(nested))).Message);
    }

    private static PackBuildResult BuildResult(int formatVersion = 1)
        => new BrandMerger().Merge(
            [TestData.Catalog("aegis", TestData.Record("aegis", "GitHub", "GitHub", "github.com"))],
            MappingSet.Empty,
            formatVersion: formatVersion);

    private static PackDocument CopyDocument(PackDocument document, PackBrand brand) => new()
    {
        FormatVersion = document.FormatVersion,
        PackId = document.PackId,
        Name = document.Name,
        Sources = document.Sources,
        Brands = [brand],
        IssuerAliases = document.IssuerAliases
    };
}
