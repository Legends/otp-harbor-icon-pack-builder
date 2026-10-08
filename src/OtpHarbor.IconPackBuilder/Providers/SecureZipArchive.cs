using System.IO.Compression;
using System.Security.Cryptography;

namespace OtpHarbor.IconPackBuilder.Providers;

public sealed class SecureZipArchive : IDisposable
{
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries;
    private readonly ArchiveLimits _limits;

    private SecureZipArchive(FileStream stream, ZipArchive archive, Dictionary<string, ZipArchiveEntry> entries, ArchiveLimits limits)
    {
        _stream = stream;
        _archive = archive;
        _entries = entries;
        _limits = limits;
    }

    public IReadOnlyCollection<string> Paths => _entries.Keys;

    public static SecureZipArchive Open(string path, ArchiveLimits? limits = null)
    {
        limits ??= new ArchiveLimits();
        if (!File.Exists(path))
            throw new InputValidationException($"Input archive does not exist: {path}");

        var info = new FileInfo(path);
        if (info.Length > limits.MaxCompressedBytes)
            throw new InputValidationException($"Archive '{path}' is {info.Length} bytes; the limit is {limits.MaxCompressedBytes} bytes.");

        FileStream? stream = null;
        ZipArchive? archive = null;
        try
        {
            stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > limits.MaxEntries)
                throw new InputValidationException($"Archive '{path}' has {archive.Entries.Count} entries; the limit is {limits.MaxEntries}.");

            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var normalized = ValidatePath(entry.FullName, path);
                if (normalized.EndsWith("/", StringComparison.Ordinal))
                    continue;
                if (!entries.TryAdd(normalized, entry))
                    throw new InputValidationException($"Archive '{path}' contains duplicate conflicting path '{normalized}'.");
                if (entry.Length > limits.MaxEntryBytes)
                    throw new InputValidationException($"Archive entry '{entry.FullName}' is {entry.Length} bytes; the per-entry limit is {limits.MaxEntryBytes}.");
                total = checked(total + entry.Length);
                if (total > limits.MaxUncompressedBytes)
                    throw new InputValidationException($"Archive '{path}' exceeds the {limits.MaxUncompressedBytes}-byte uncompressed size limit.");
                if (entry.Length > 1_048_576 && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > limits.MaxCompressionRatio))
                    throw new InputValidationException($"Archive entry '{entry.FullName}' exceeds the {limits.MaxCompressionRatio:0.#}:1 compression-ratio limit.");
            }

            return new SecureZipArchive(stream, archive, entries, limits);
        }
        catch
        {
            archive?.Dispose();
            stream?.Dispose();
            throw;
        }
    }

    public string FindRequired(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var matches = _entries.Keys
            .Where(x => x.Equals(normalized, StringComparison.OrdinalIgnoreCase)
                || x.EndsWith('/' + normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InputValidationException($"Unsupported source structure: required file '{relativePath}' was not found."),
            _ => throw new InputValidationException($"Unsupported source structure: multiple files match '{relativePath}'.")
        };
    }

    public string? FindOptional(string relativePath)
    {
        try { return FindRequired(relativePath); }
        catch (InputValidationException ex) when (ex.Message.Contains("was not found", StringComparison.Ordinal)) { return null; }
    }

    public string ResolveRelative(string metadataPath, string referencedPath)
    {
        var cleanReference = ValidatePath(referencedPath, "metadata reference");
        var rootLength = metadataPath.Length - Path.GetFileName(metadataPath).Length;
        var rooted = metadataPath[..rootLength] + cleanReference;
        if (_entries.ContainsKey(rooted))
            return rooted;
        if (_entries.ContainsKey(cleanReference))
            return cleanReference;
        var suffixMatches = _entries.Keys
            .Where(x => x.EndsWith('/' + cleanReference, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (suffixMatches.Length == 1)
            return suffixMatches[0];
        if (suffixMatches.Length > 1)
            throw new InputValidationException($"Multiple archive files match referenced icon '{referencedPath}'.");
        throw new InputValidationException($"Missing referenced icon file '{referencedPath}' (from '{metadataPath}').");
    }

    public async Task<byte[]> ReadAsync(string path, long? maximumBytes = null, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(path, out var entry))
            throw new InputValidationException($"Archive entry '{path}' was not found.");
        var max = maximumBytes ?? _limits.MaxEntryBytes;
        if (entry.Length > max)
            throw new InputValidationException($"Archive entry '{path}' is {entry.Length} bytes; the allowed limit is {max}.");
        await using var source = entry.Open();
        using var destination = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        await source.CopyToAsync(destination, cancellationToken);
        if (destination.Length > max)
            throw new InputValidationException($"Archive entry '{path}' expanded beyond its allowed limit.");
        return destination.ToArray();
    }

    public async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default)
        => System.Text.Encoding.UTF8.GetString(await ReadAsync(path, cancellationToken: cancellationToken));

    public static async Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose()
    {
        _archive.Dispose();
        _stream.Dispose();
    }

    private static string ValidatePath(string path, string source)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0)
            throw new InputValidationException($"Unsafe empty or NUL-containing archive path in '{source}'.");
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.StartsWith("//", StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
            throw new InputValidationException($"Rejected unsafe absolute archive path '{path}' in '{source}'.");
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(x => x is "." or ".."))
            throw new InputValidationException($"Rejected unsafe parent-directory archive path '{path}' in '{source}'.");
        return string.Join('/', segments) + (normalized.EndsWith('/') ? "/" : string.Empty);
    }
}
