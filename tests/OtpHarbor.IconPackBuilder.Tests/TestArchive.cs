using System.IO.Compression;
using System.Text;

namespace OtpHarbor.IconPackBuilder.Tests;

internal sealed class TestArchive : IDisposable
{
    public TestArchive(IReadOnlyDictionary<string, string> entries)
        : this(entries.ToDictionary(x => x.Key, x => Encoding.UTF8.GetBytes(x.Value), StringComparer.Ordinal)) { }

    public TestArchive(IReadOnlyDictionary<string, byte[]> entries)
    {
        DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "otp-harbor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        Path = System.IO.Path.Combine(DirectoryPath, "source.zip");
        using var file = File.Create(Path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = zip.CreateEntry(item.Key, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(item.Value);
        }
    }

    public string DirectoryPath { get; }
    public string Path { get; }

    public string FilePath(string name) => System.IO.Path.Combine(DirectoryPath, name);

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}

internal static class TestData
{
    public const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><path d=\"M0 0h1v1z\"/></svg>";

    public static SourceRecord Record(string provider, string sourceId, string name, params string[] aliases)
        => new(provider, sourceId, name, aliases.Prepend(name).ToArray(), provider + "/" + sourceId + ".svg",
            Encoding.UTF8.GetBytes(Svg), provider == "aegis" ? "primary" : "default",
            new SortedDictionary<string, string?>(StringComparer.Ordinal) { ["source"] = sourceId });

    public static ProviderCatalog Catalog(string provider, params SourceRecord[] records)
        => new(provider, provider + ".zip", new string('a', 64),
            new SortedDictionary<string, string?>(StringComparer.Ordinal), records, []);

    public static MappingSet Mappings(params CanonicalBrandMapping[] brands)
        => new(brands, new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, SourceOverride>());
}
