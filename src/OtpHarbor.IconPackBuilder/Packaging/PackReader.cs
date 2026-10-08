using System.Text.Json;
using OtpHarbor.IconPackBuilder.Normalization;
using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Packaging;

public sealed record ReadPack(PackDocument Document, IReadOnlyDictionary<string, byte[]> Files);

public static class PackReader
{
    public static async Task<ReadPack> ReadAndValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        using var archive = SecureZipArchive.Open(path, new ArchiveLimits(MaxEntries: 50_000));
        var packPath = archive.FindRequired("pack.json");
        PackDocument document;
        try
        {
            document = JsonSerializer.Deserialize<PackDocument>(await archive.ReadAsync(packPath, 20 * 1024 * 1024, cancellationToken), PackSerializer.JsonOptions)
                ?? throw new InputValidationException("pack.json is empty.");
        }
        catch (JsonException ex) { throw new InputValidationException($"Malformed pack.json: {ex.Message}"); }
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var filePath in archive.Paths.Where(x => x != packPath).OrderBy(x => x, StringComparer.Ordinal))
            files[filePath] = await archive.ReadAsync(filePath, cancellationToken: cancellationToken);
        Validate(document, files);
        return new ReadPack(document, files);
    }

    public static void Validate(PackDocument document, IReadOnlyDictionary<string, byte[]> files)
    {
        if (document.FormatVersion != 1) throw new InputValidationException($"Unsupported pack formatVersion {document.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(document.PackId) || CanonicalId.FromName(document.PackId) != document.PackId
            || !ProviderHelpers.IsSafeDisplayText(document.Name))
            throw new InputValidationException("pack.json requires packId and name.");
        if (document.Sources is null || document.Sources.Count == 0)
            throw new InputValidationException("pack.json requires at least one source.");
        if (document.Brands is null || document.Brands.Count == 0)
            throw new InputValidationException("pack.json requires at least one brand.");
        if (document.IssuerAliases is null || document.IssuerAliases.Count == 0)
            throw new InputValidationException("pack.json requires at least one issuer alias.");

        var expectedFiles = new HashSet<string>(StringComparer.Ordinal);
        var sourceProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in document.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Provider)
                || CanonicalId.FromName(source.Provider) != source.Provider
                || !sourceProviders.Add(source.Provider))
                throw new InputValidationException($"Invalid or duplicate source provider '{source.Provider}'.");
            if (string.IsNullOrWhiteSpace(source.InputFileName)
                || Path.GetFileName(source.InputFileName) != source.InputFileName
                || source.Sha256.Length != 64
                || source.Sha256.Any(x => x is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                || source.SourceUrl is not null && (!Uri.TryCreate(source.SourceUrl, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                || source.Metadata is null
                || source.LicenseFiles is null)
                throw new InputValidationException($"Source '{source.Provider}' contains invalid provenance metadata.");
            foreach (var license in source.LicenseFiles)
            {
                if (!license.StartsWith($"licenses/{source.Provider}/", StringComparison.Ordinal)
                    || !expectedFiles.Add(license)
                    || !files.TryGetValue(license, out var content)
                    || content.Length is <= 0 or > 1_048_576)
                    throw new InputValidationException($"Source '{source.Provider}' references invalid license '{license}'.");
            }
        }

        var brandIds = new HashSet<string>(StringComparer.Ordinal);
        var expectedAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var brand in document.Brands)
        {
            if (!brandIds.Add(brand.Id)) throw new InputValidationException($"Duplicate brand ID '{brand.Id}'.");
            if (CanonicalId.FromName(brand.Id) != brand.Id) throw new InputValidationException($"Invalid canonical brand ID '{brand.Id}'.");
            if (!ProviderHelpers.IsSafeDisplayText(brand.DisplayName)
                || brand.BackgroundColor.Length != 7 || brand.BackgroundColor[0] != '#'
                || !ProviderHelpers.IsHexColor(brand.BackgroundColor[1..])
                || brand.BackgroundColor != brand.BackgroundColor.ToUpperInvariant())
                throw new InputValidationException($"Brand '{brand.Id}' has invalid backgroundColor '{brand.BackgroundColor}'.");
            if (brand.Icon != $"icons/{brand.Id}.svg"
                || !expectedFiles.Add(brand.Icon)
                || !files.TryGetValue(brand.Icon, out var svg))
                throw new InputValidationException($"Brand '{brand.Id}' references missing icon or invalid icon path '{brand.Icon}'.");
            ProviderHelpers.ValidateSvg(svg, "otp-harbor-icons", brand.Icon, 1024 * 1024);
            SvgNormalizer.ValidateCanonical(svg, brand.Icon);
            if (brand.IssuerAliases is null || brand.IssuerAliases.Count == 0)
                throw new InputValidationException($"Brand '{brand.Id}' has no issuer aliases.");
            if (brand.Sources is null || brand.Sources.Count == 0 || brand.SelectedSource is null)
                throw new InputValidationException($"Brand '{brand.Id}' has no source provenance.");
            ValidateSourceReference(brand.SelectedSource.Provider, brand.SelectedSource.SourceId,
                brand.SelectedSource.Metadata, brand.Id);
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in brand.Sources)
            {
                ValidateSourceReference(source.Provider, source.SourceId, source.Metadata, brand.Id);
                if (!sourceProviders.Contains(source.Provider))
                    throw new InputValidationException($"Brand '{brand.Id}' references unknown provider '{source.Provider}'.");
                if (!references.Add(source.Provider + "/" + source.SourceId))
                    throw new InputValidationException($"Brand '{brand.Id}' contains duplicate source '{source.Provider}/{source.SourceId}'.");
            }
            if (!brand.Sources.Any(x => x.Provider == brand.SelectedSource.Provider && x.SourceId == brand.SelectedSource.SourceId))
                throw new InputValidationException($"Brand '{brand.Id}' selectedSource is not present in sources.");
            foreach (var alias in brand.IssuerAliases)
            {
                if (!ProviderHelpers.IsSafeDisplayText(alias, 256))
                    throw new InputValidationException($"Brand '{brand.Id}' contains an invalid issuer alias.");
                var key = IssuerNormalizer.Normalize(alias);
                if (key.Length == 0)
                    throw new InputValidationException($"Brand '{brand.Id}' contains an empty normalized issuer alias.");
                if (!expectedAliases.TryAdd(key, brand.Id) && expectedAliases[key] != brand.Id)
                    throw new InputValidationException($"Normalized issuer alias '{key}' is ambiguous.");
            }
        }
        var aliasKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in document.IssuerAliases)
        {
            if (string.IsNullOrWhiteSpace(alias.Key) || alias.Key.Length > 512
                || IssuerNormalizer.Normalize(alias.Key) != alias.Key
                || !aliasKeys.Add(alias.Key))
                throw new InputValidationException($"Invalid or duplicate issuer alias key '{alias.Key}'.");
            if (!brandIds.Contains(alias.BrandId)) throw new InputValidationException($"Issuer alias '{alias.Key}' references unknown brand '{alias.BrandId}'.");
            if (!expectedAliases.TryGetValue(alias.Key, out var expectedBrand) || expectedBrand != alias.BrandId)
                throw new InputValidationException($"Issuer alias '{alias.Key}' does not match the canonical brand aliases.");
        }
        if (aliasKeys.Count != expectedAliases.Count)
            throw new InputValidationException("The issuer alias index is incomplete.");
        foreach (var file in files.Keys)
            if (!expectedFiles.Contains(file))
                throw new InputValidationException($"Pack contains unexpected or unreferenced file '{file}'.");
    }

    private static void ValidateSourceReference(
        string provider,
        string sourceId,
        IReadOnlyDictionary<string, string?> metadata,
        string brandId)
    {
        if (string.IsNullOrWhiteSpace(provider)
            || CanonicalId.FromName(provider) != provider
            || !ProviderHelpers.IsSafeDisplayText(sourceId)
            || metadata is null)
            throw new InputValidationException($"Brand '{brandId}' contains invalid source provenance.");
    }
}
