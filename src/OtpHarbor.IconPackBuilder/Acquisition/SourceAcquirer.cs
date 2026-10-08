using System.Security.Cryptography;
using System.Text.Json;
using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Acquisition;

public sealed record SourceAcquisitionOptions(string CacheDirectory, bool Offline = false, bool Refresh = false);

public interface ISourceAcquirer
{
    Task<IReadOnlyList<IconSourceInput>> AcquireAsync(
        IReadOnlyDictionary<string, string> localOverrides,
        SourceAcquisitionOptions options,
        CancellationToken cancellationToken);
}

public sealed class SourceAcquirer : ISourceAcquirer, IDisposable
{
    // The cache format is part of the directory name so an open diagnostics report from an older
    // format cannot prevent a newer builder from publishing its immutable cache generation.
    private const int ManifestVersion = 5;
    private readonly IReadOnlyDictionary<string, IUpstreamDefinition> _definitions;
    private readonly IRemoteContentClient _client;
    private readonly bool _ownsClient;

    public SourceAcquirer(IEnumerable<IUpstreamDefinition>? definitions = null, IRemoteContentClient? client = null)
    {
        definitions ??= [new AegisUpstreamDefinition(), new SimpleIconsUpstreamDefinition(), new DashboardIconsUpstreamDefinition()];
        _definitions = definitions.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        _client = client ?? new RemoteContentClient();
        _ownsClient = client is null;
    }

    public async Task<IReadOnlyList<IconSourceInput>> AcquireAsync(
        IReadOnlyDictionary<string, string> localOverrides,
        SourceAcquisitionOptions options,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.CacheDirectory);
        var results = new List<IconSourceInput>();
        foreach (var definition in _definitions.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (localOverrides.TryGetValue(definition.Id, out var localPath))
            {
                if (!File.Exists(localPath))
                    throw new InputValidationException($"{definition.Id}: local source archive does not exist: {localPath}");
                results.Add(new IconSourceInput(definition.Id, Path.GetFullPath(localPath)));
                continue;
            }

            if (options.Offline)
            {
                results.Add(await FindOfflineCacheAsync(definition.Id, options.CacheDirectory, cancellationToken)
                    ?? throw new InvalidDataException($"{definition.Id}: offline mode requires a valid cached source or an explicit --{definition.Id} override."));
                continue;
            }

            ResolvedUpstream resolved;
            try { resolved = await definition.ResolveSupportedAsync(_client, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException)
            {
                throw new InvalidDataException($"{definition.Id}: failed to resolve the supported upstream source: {ex.Message}", ex);
            }
            ValidateResolvedSource(definition.Id, resolved);
            var cached = await TryReadCacheAsync(
                CacheDirectory(options.CacheDirectory, resolved), definition.Id, resolved.Identity, cancellationToken);
            if (cached is not null && !options.Refresh)
            {
                results.Add(cached);
                continue;
            }
            results.Add(await DownloadAndCacheAsync(definition, resolved, options.CacheDirectory, cancellationToken));
        }
        return results;
    }

    private async Task<IconSourceInput> DownloadAndCacheAsync(
        IUpstreamDefinition definition,
        ResolvedUpstream resolved,
        string cacheRoot,
        CancellationToken cancellationToken)
    {
        var finalDirectory = CacheDirectory(cacheRoot, resolved);
        var providerDirectory = Path.GetDirectoryName(finalDirectory)!;
        Directory.CreateDirectory(providerDirectory);
        var temporaryDirectory = Path.Combine(providerDirectory, ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var archivePath = Path.Combine(temporaryDirectory, "source.zip");
        try
        {
            try { await definition.CreateSourceArchiveAsync(resolved, archivePath, _client, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
            {
                throw new InvalidDataException($"{definition.Id}: failed to download pinned source '{resolved.Identity}' from '{resolved.DownloadUrl}': {ex.Message}", ex);
            }

            // Opening applies path, count, size, duplicate, and decompression-ratio checks before cache publication.
            using (SecureZipArchive.Open(archivePath)) { }
            var sha256 = await SecureZipArchive.Sha256Async(archivePath, cancellationToken);
            var manifest = new CacheManifest(ManifestVersion, definition.Id, resolved.Identity, resolved.Version,
                resolved.Revision, resolved.SourceUrl, resolved.DownloadUrl, sha256);
            await File.WriteAllBytesAsync(Path.Combine(temporaryDirectory, "cache.json"),
                JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions), cancellationToken);

            if (Directory.Exists(finalDirectory))
            {
                var oldDirectory = finalDirectory + ".old-" + Guid.NewGuid().ToString("N");
                EnsureChildPath(providerDirectory, oldDirectory);
                Directory.Move(finalDirectory, oldDirectory);
                try { Directory.Move(temporaryDirectory, finalDirectory); }
                catch { Directory.Move(oldDirectory, finalDirectory); throw; }
                Directory.Delete(oldDirectory, recursive: true);
            }
            else
            {
                Directory.Move(temporaryDirectory, finalDirectory);
            }
            return (await TryReadCacheAsync(finalDirectory, definition.Id, resolved.Identity, cancellationToken))
                ?? throw new InvalidDataException($"{definition.Id}: the newly written cache entry failed validation.");
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                EnsureChildPath(providerDirectory, temporaryDirectory);
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static async Task<IconSourceInput?> FindOfflineCacheAsync(string provider, string cacheRoot, CancellationToken cancellationToken)
    {
        var providerDirectory = Path.Combine(cacheRoot, provider);
        if (!Directory.Exists(providerDirectory)) return null;
        foreach (var directory in Directory.EnumerateDirectories(providerDirectory)
                     .Where(x => !Path.GetFileName(x).StartsWith(".", StringComparison.Ordinal))
                     .OrderByDescending(Directory.GetLastWriteTimeUtc)
                     .ThenBy(x => x, StringComparer.Ordinal))
        {
            var cached = await TryReadCacheAsync(directory, provider, expectedIdentity: null, cancellationToken);
            if (cached is not null && cached.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)) return cached;
        }
        return null;
    }

    private static async Task<IconSourceInput?> TryReadCacheAsync(
        string directory,
        string expectedProvider,
        string? expectedIdentity,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory, "cache.json");
        var archivePath = Path.Combine(directory, "source.zip");
        if (!File.Exists(manifestPath) || !File.Exists(archivePath)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<CacheManifest>(await File.ReadAllBytesAsync(manifestPath, cancellationToken), JsonOptions);
            if (manifest is null || manifest.FormatVersion != ManifestVersion || string.IsNullOrWhiteSpace(manifest.Provider)
                || !manifest.Provider.Equals(expectedProvider, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(manifest.Identity)
                || expectedIdentity is not null && !manifest.Identity.Equals(expectedIdentity, StringComparison.Ordinal)
                || manifest.Sha256.Length != 64
                || !IsHttpsUrl(manifest.SourceUrl)
                || !IsHttpsUrl(manifest.DownloadUrl))
                return null;
            var actualHash = await SecureZipArchive.Sha256Async(archivePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(manifest.Sha256), Convert.FromHexString(actualHash))) return null;
            using (SecureZipArchive.Open(archivePath)) { }
            return new IconSourceInput(manifest.Provider, archivePath, manifest.Version, manifest.Revision, manifest.SourceUrl);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InputValidationException)
        {
            return null;
        }
    }

    private static string CacheDirectory(string root, ResolvedUpstream source)
    {
        var safe = string.Concat(source.Identity.Select(x => char.IsAsciiLetterOrDigit(x) || x is '.' or '-' or '_' ? x : '_'));
        if (!safe.Equals(source.Identity, StringComparison.Ordinal))
            safe += "-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source.Identity)))[..12].ToLowerInvariant();
        return Path.Combine(root, source.Provider, $"{safe}.cache-v{ManifestVersion}");
    }

    private static void ValidateResolvedSource(string provider, ResolvedUpstream source)
    {
        if (!source.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(source.Identity)
            || source.Identity.Length > 256
            || !IsHttpsUrl(source.SourceUrl)
            || !IsHttpsUrl(source.DownloadUrl))
            throw new InvalidDataException($"{provider}: upstream resolution returned invalid identity or provenance metadata.");
    }

    private static bool IsHttpsUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static void EnsureChildPath(string parent, string child)
    {
        var fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
        var fullChild = Path.GetFullPath(child);
        if (!fullChild.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to modify cache path outside '{fullParent}'.");
    }

    public void Dispose()
    {
        if (_ownsClient && _client is IDisposable disposable) disposable.Dispose();
    }

    private sealed record CacheManifest(
        int FormatVersion,
        string Provider,
        string Identity,
        string? Version,
        string? Revision,
        string SourceUrl,
        string DownloadUrl,
        string Sha256);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };
}
