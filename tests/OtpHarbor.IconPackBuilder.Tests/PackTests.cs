using System.IO.Compression;
using System.Buffers.Binary;
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
        Assert.Null(brand.Icon);
        Assert.Equal("runtime/foreground-128/github.png", brand.RuntimeIcon!.Path);
        Assert.Equal(DeterministicPng.Sha256(read.Files[brand.RuntimeIcon.Path]), brand.RuntimeIcon.Sha256);
        using var file = File.OpenRead(first);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.Equal([
            "pack.json", "runtime/", "runtime/foreground-128/", "runtime/foreground-128/github.png", "licenses/"
        ], zip.Entries.Select(x => x.FullName));
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));
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
        var brand = pack.RootElement.GetProperty("brands")[0];
        Assert.False(brand.TryGetProperty("icon", out _));
        var runtime = brand.GetProperty("runtimeIcon");
        Assert.Equal(128, runtime.GetProperty("width").GetInt32());
        Assert.Equal("otp-harbor-foreground-v1", runtime.GetProperty("renderProfile").GetString());
        var brandSchema = schema.RootElement.GetProperty("$defs").GetProperty("brand");
        Assert.False(brandSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.DoesNotContain("icon", brandSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.False(brandSchema.GetProperty("properties").TryGetProperty("icon", out _));
        var runtimeSchema = schema.RootElement.GetProperty("$defs").GetProperty("runtimeIcon");
        Assert.False(runtimeSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["path", "mediaType", "width", "height", "sha256", "renderProfile"],
            runtimeSchema.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("image/png", runtimeSchema.GetProperty("properties").GetProperty("mediaType").GetProperty("const").GetString());
        Assert.Equal(128, runtimeSchema.GetProperty("properties").GetProperty("width").GetProperty("const").GetInt32());
        Assert.Equal(128, runtimeSchema.GetProperty("properties").GetProperty("height").GetProperty("const").GetInt32());
        Assert.Equal("otp-harbor-foreground-v1",
            runtimeSchema.GetProperty("properties").GetProperty("renderProfile").GetProperty("const").GetString());
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
    public async Task VersionTwoReaderRejectsFormatOneIconMember()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var result = BuildResult(2);
        var brand = Assert.Single(result.Document.Brands);
        var path = fixture.FilePath("v2-with-icon-member.otphicons");
        await PackSerializer.WriteAsync(path, result with
        {
            Document = CopyDocument(result.Document, brand with { Icon = "icons/github.svg" })
        });

        var exception = await Assert.ThrowsAsync<InputValidationException>(() => PackReader.ReadAndValidateAsync(path));

        Assert.Contains("must not contain", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task VersionTwoReaderRejectsBlankRuntimePng()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var result = WithRuntimePng(BuildResult(2), DeterministicPng.EncodeRgba(128, 128, new byte[128 * 128 * 4]));
        var path = fixture.FilePath("blank-runtime.otphicons");
        await PackSerializer.WriteAsync(path, result);

        var exception = await Assert.ThrowsAsync<InputValidationException>(() => PackReader.ReadAndValidateAsync(path));

        Assert.Contains("blank", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(24, 16)] // bit depth
    [InlineData(25, 2)]  // color type
    [InlineData(26, 1)]  // compression method
    [InlineData(27, 1)]  // PNG filter method
    [InlineData(28, 1)]  // interlace method
    public void PngValidatorRejectsUnsupportedIhdrProfile(int offset, byte value)
    {
        var png = VisiblePng();
        png[offset] = value;
        RewriteChunkCrc(png, 8);

        Assert.Throws<InputValidationException>(() =>
            DeterministicPng.DecodeAndValidate(png, 128, 128, "invalid-profile.png"));
    }

    [Fact]
    public void PngValidatorRejectsCrcAncillaryChunkTrailingDataFilterAndSizeViolations()
    {
        var corruptCrc = VisiblePng();
        corruptCrc[29] ^= 0xff;
        Assert.Contains("CRC", Assert.Throws<InputValidationException>(() =>
            DeterministicPng.DecodeAndValidate(corruptCrc, 128, 128, "crc.png")).Message,
            StringComparison.OrdinalIgnoreCase);

        var ancillary = InsertChunkBeforeIend(VisiblePng(), "tEXt", [1]);
        Assert.Contains("forbidden", Assert.Throws<InputValidationException>(() =>
            DeterministicPng.DecodeAndValidate(ancillary, 128, 128, "metadata.png")).Message,
            StringComparison.OrdinalIgnoreCase);

        var trailing = VisiblePng().Concat(new byte[] { 0 }).ToArray();
        Assert.Contains("end chunk", Assert.Throws<InputValidationException>(() =>
            DeterministicPng.DecodeAndValidate(trailing, 128, 128, "trailing.png")).Message,
            StringComparison.OrdinalIgnoreCase);

        var filtered = BuildPngWithScanlineFilter(1);
        Assert.Contains("filter type", Assert.Throws<InputValidationException>(() =>
            DeterministicPng.DecodeAndValidate(filtered, 128, 128, "filtered.png")).Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains("1 MiB", Assert.Throws<InputValidationException>(() =>
            DeterministicPng.DecodeAndValidate(new byte[DeterministicPng.MaximumEncodedBytes + 1], 128, 128,
                "oversized.png")).Message);
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

    private static PackBuildResult WithRuntimePng(PackBuildResult result, byte[] png)
    {
        var brand = Assert.Single(result.Document.Brands);
        return result with
        {
            RuntimeIcons = new Dictionary<string, byte[]> { [brand.RuntimeIcon!.Path] = png },
            Document = CopyDocument(result.Document, brand with
            {
                RuntimeIcon = brand.RuntimeIcon with { Sha256 = DeterministicPng.Sha256(png) }
            })
        };
    }

    private static byte[] VisiblePng()
    {
        var rgba = new byte[128 * 128 * 4];
        rgba[0] = rgba[1] = rgba[2] = rgba[3] = 255;
        return DeterministicPng.EncodeRgba(128, 128, rgba);
    }

    private static byte[] BuildPngWithScanlineFilter(byte filter)
    {
        var scanlines = new byte[128 * (1 + 128 * 4)];
        for (var y = 0; y < 128; y++) scanlines[y * 513] = filter;
        scanlines[1] = scanlines[2] = scanlines[3] = scanlines[4] = 255;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(scanlines);
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 128);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], 128);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] InsertChunkBeforeIend(byte[] png, string type, byte[] data)
    {
        var iendOffset = png.Length - 12;
        using var output = new MemoryStream();
        output.Write(png, 0, iendOffset);
        WriteChunk(output, type, data);
        output.Write(png, iendOffset, 12);
        return output.ToArray();
    }

    private static void RewriteChunkCrc(byte[] png, int chunkOffset)
    {
        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(chunkOffset, 4));
        var crc = Crc32(png.AsSpan(chunkOffset + 4, 4 + length));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(chunkOffset + 8 + length, 4), crc);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(length, Crc32(typeBytes.Concat(data.ToArray()).ToArray()));
        output.Write(length);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xffffffffu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        return ~crc;
    }

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
