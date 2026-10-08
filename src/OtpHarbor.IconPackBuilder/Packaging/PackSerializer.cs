using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OtpHarbor.IconPackBuilder.Packaging;

public static class PackSerializer
{
    private static readonly DateTimeOffset StableTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static async Task WriteAsync(string path, PackBuildResult result, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        await using var file = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
        await WriteEntryAsync(archive, "pack.json", JsonSerializer.SerializeToUtf8Bytes(result.Document, JsonOptions), cancellationToken);
        CreateDirectoryEntry(archive, "icons/");
        foreach (var icon in result.Icons.OrderBy(x => x.Key, StringComparer.Ordinal))
            await WriteEntryAsync(archive, icon.Key, icon.Value, cancellationToken);
        CreateDirectoryEntry(archive, "licenses/");
        foreach (var license in result.Licenses.OrderBy(x => x.Key, StringComparer.Ordinal))
            await WriteEntryAsync(archive, license.Key, license.Value, cancellationToken);
    }

    private static void CreateDirectoryEntry(ZipArchive archive, string path)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        entry.LastWriteTime = StableTimestamp;
        entry.ExternalAttributes = 0;
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.SmallestSize);
        entry.LastWriteTime = StableTimestamp;
        entry.ExternalAttributes = 0;
        await using var stream = entry.Open();
        await stream.WriteAsync(content, cancellationToken);
    }
}
