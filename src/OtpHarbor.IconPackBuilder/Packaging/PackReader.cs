using System.Text.Json;
using System.Security.Cryptography;
using OtpHarbor.IconPackBuilder.Normalization;
using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Packaging;

public sealed record ReadPack(PackDocument Document, IReadOnlyDictionary<string, byte[]> Files);

public static class PackReader
{
    public static async Task<ReadPack> ReadAndValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        const long maximumPackJsonBytes = 128L * 1024 * 1024;
        using var archive = SecureZipArchive.Open(path, new ArchiveLimits(
            MaxEntries: 100_000,
            MaxEntryBytes: maximumPackJsonBytes,
            MaxUncompressedBytes: 512L * 1024 * 1024,
            RequireCanonicalPaths: true));
        var packPath = archive.FindRequired("pack.json");
        if (!string.Equals(packPath, "pack.json", StringComparison.Ordinal))
            throw new InputValidationException("The pack manifest must be the canonical root path 'pack.json'.");
        PackDocument document;
        try
        {
            var manifestBytes = await archive.ReadAsync(packPath, maximumPackJsonBytes, cancellationToken);
            ValidateVersionSpecificJsonShape(manifestBytes);
            document = JsonSerializer.Deserialize<PackDocument>(manifestBytes, PackSerializer.JsonOptions)
                ?? throw new InputValidationException("pack.json is empty.");
        }
        catch (JsonException ex) { throw new InputValidationException($"Malformed pack.json: {ex.Message}"); }
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var filePath in archive.Paths.Where(x => x != packPath).OrderBy(x => x, StringComparer.Ordinal))
            files[filePath] = await archive.ReadAsync(filePath, 1_048_576, cancellationToken);
        Validate(document, files);
        return new ReadPack(document, files);
    }

    private static void ValidateVersionSpecificJsonShape(byte[] manifestBytes)
    {
        using var json = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = json.RootElement;
        if (!root.TryGetProperty("formatVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var formatVersion)
            || formatVersion is not (2 or 3)
            || !root.TryGetProperty("brands", out var brands)
            || brands.ValueKind != JsonValueKind.Array)
            return;
        foreach (var brand in brands.EnumerateArray())
        {
            if (brand.ValueKind != JsonValueKind.Object) continue;
            if (brand.TryGetProperty("icon", out _))
                throw new InputValidationException($"Version {formatVersion} brand manifests must not contain the format-1 icon member.");
            if (formatVersion == 2 && brand.TryGetProperty("appearances", out _))
                throw new InputValidationException("Version 2 brand manifests must not contain version-3 appearances.");
            if (formatVersion == 3 && (brand.TryGetProperty("backgroundColor", out _)
                || brand.TryGetProperty("runtimeIcon", out _)))
                throw new InputValidationException("Version 3 brand manifests must not contain legacy backgroundColor or runtimeIcon members.");
        }
    }

    public static void Validate(PackDocument document, IReadOnlyDictionary<string, byte[]> files)
    {
        if (document.FormatVersion is not (1 or 2 or 3))
            throw new InputValidationException($"Unsupported pack formatVersion {document.FormatVersion}.");
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
        var validatedRuntimeHashes = new HashSet<string>(StringComparer.Ordinal);
        var validatedRuntimeProfiles = new HashSet<string>(StringComparer.Ordinal);
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
        string? previousBrandId = null;
        foreach (var brand in document.Brands)
        {
            if (document.FormatVersion is 2 or 3 && previousBrandId is not null
                && string.CompareOrdinal(previousBrandId, brand.Id) >= 0)
                throw new InputValidationException($"Version {document.FormatVersion} brands are not in canonical ID order.");
            previousBrandId = brand.Id;
            if (!brandIds.Add(brand.Id)) throw new InputValidationException($"Duplicate brand ID '{brand.Id}'.");
            if (CanonicalId.FromName(brand.Id) != brand.Id) throw new InputValidationException($"Invalid canonical brand ID '{brand.Id}'.");
            if (!ProviderHelpers.IsSafeDisplayText(brand.DisplayName))
                throw new InputValidationException($"Brand '{brand.Id}' has an invalid display name.");
            if (document.FormatVersion is 1 or 2
                && !IsCanonicalColor(brand.BackgroundColor))
                throw new InputValidationException($"Brand '{brand.Id}' has invalid backgroundColor '{brand.BackgroundColor}'.");
            if (document.FormatVersion == 1)
            {
                var expectedIcon = $"icons/{brand.Id}.svg";
                if (brand.Icon != expectedIcon
                    || !expectedFiles.Add(expectedIcon)
                    || !files.TryGetValue(expectedIcon, out var svg))
                    throw new InputValidationException($"Brand '{brand.Id}' references missing icon or invalid icon path '{brand.Icon}'.");
                ProviderHelpers.ValidateSvg(svg, "otp-harbor-icons", expectedIcon, 1024 * 1024);
                SvgNormalizer.ValidateCanonical(svg, expectedIcon);
                if (brand.RuntimeIcon is not null || brand.Appearances is not null)
                    throw new InputValidationException($"Version 1 brand '{brand.Id}' must not contain runtime appearance metadata.");
            }
            else if (document.FormatVersion == 2)
            {
                if (brand.Icon is not null)
                    throw new InputValidationException($"Version 2 brand '{brand.Id}' must not contain the format-1 icon member.");
                ValidateRuntimeIcon(brand, files, expectedFiles);
                if (brand.Appearances is not null)
                    throw new InputValidationException($"Version 2 brand '{brand.Id}' must not contain version-3 appearances.");
            }
            else
            {
                if (brand.Icon is not null || brand.RuntimeIcon is not null || brand.BackgroundColor is not null)
                    throw new InputValidationException($"Version 3 brand '{brand.Id}' contains a legacy icon member.");
                ValidateAppearances(brand, files, expectedFiles, sourceProviders,
                    validatedRuntimeHashes, validatedRuntimeProfiles);
            }
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

    public static void ValidateV2RuntimeVisuals(
        ReadPack pack,
        IReadOnlyDictionary<string, byte[]> canonicalIcons)
    {
        if (pack.Document.FormatVersion != 2)
            throw new ArgumentException("Runtime visual validation requires a format-2 pack.", nameof(pack));
        var expectedCanonicalPaths = pack.Document.Brands
            .Select(brand => $"icons/{brand.Id}.svg")
            .ToHashSet(StringComparer.Ordinal);
        if (canonicalIcons.Count != expectedCanonicalPaths.Count
            || canonicalIcons.Keys.Any(path => !expectedCanonicalPaths.Contains(path)))
            throw new InputValidationException("Internal canonical SVG coverage does not match the version 2 manifest.");

        foreach (var brand in pack.Document.Brands)
        {
            var canonicalPath = $"icons/{brand.Id}.svg";
            if (!canonicalIcons.TryGetValue(canonicalPath, out var canonicalSvg))
                throw new InputValidationException($"Brand '{brand.Id}' has no internal canonical SVG reference for runtime verification.");
            SvgNormalizer.ValidateCanonical(canonicalSvg, canonicalPath);
            var runtimePath = brand.RuntimeIcon!.Path;
            var metrics = SvgVisualVerifier.CompareCanonicalToRuntimePng(
                canonicalSvg, pack.Files[runtimePath], brand.BackgroundColor!);
            if (!metrics.Resolved)
                throw new InputValidationException($"Runtime PNG '{runtimePath}' does not faithfully match the internal canonical SVG reference.");
        }
    }

    public static void ValidateV3RuntimeVisuals(
        ReadPack pack,
        IReadOnlyList<RuntimeVisualReference> references)
    {
        if (pack.Document.FormatVersion != 3)
            throw new ArgumentException("Themed runtime visual validation requires a format-3 pack.", nameof(pack));
        var expected = pack.Document.Brands
            .SelectMany(brand => new[]
            {
                new KeyValuePair<string, ThemeAppearance>($"{brand.Id}/lightTheme", brand.Appearances!.LightTheme),
                new KeyValuePair<string, ThemeAppearance>($"{brand.Id}/darkTheme", brand.Appearances!.DarkTheme)
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var actual = references.Select(reference => $"{reference.BrandId}/{reference.Appearance}").ToArray();
        if (actual.Length != expected.Count || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length
            || actual.Any(key => !expected.ContainsKey(key)))
            throw new InputValidationException("Internal canonical SVG coverage does not match the version 3 manifest appearances.");
        foreach (var reference in references)
        {
            var appearance = expected[$"{reference.BrandId}/{reference.Appearance}"];
            if (!reference.AssetPath.Equals(appearance.Asset.Path, StringComparison.Ordinal)
                || !reference.ExpectedSha256.Equals(appearance.Asset.Sha256, StringComparison.Ordinal)
                || !reference.AssetPath.Equals($"runtime/appearance-128/{reference.ExpectedSha256}.png", StringComparison.Ordinal))
                throw new InputValidationException($"Runtime visual reference for '{reference.BrandId}/{reference.Appearance}' does not match the validated manifest asset.");
            if (!pack.Files.TryGetValue(reference.AssetPath, out var png))
                throw new InputValidationException($"Runtime visual reference '{reference.AssetPath}' is missing from the pack.");
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(png), Convert.FromHexString(reference.ExpectedSha256)))
                throw new InputValidationException($"Runtime visual reference '{reference.AssetPath}' does not match its builder-verified bytes.");
        }
    }

    private static void ValidateRuntimeIcon(
        PackBrand brand,
        IReadOnlyDictionary<string, byte[]> files,
        ISet<string> expectedFiles)
    {
        var runtime = brand.RuntimeIcon
            ?? throw new InputValidationException($"Version 2 brand '{brand.Id}' has no runtime icon.");
        var expectedPath = $"runtime/foreground-128/{brand.Id}.png";
        if (string.IsNullOrEmpty(runtime.Path)
            || string.IsNullOrEmpty(runtime.MediaType)
            || string.IsNullOrEmpty(runtime.Sha256)
            || string.IsNullOrEmpty(runtime.RenderProfile)
            || !string.Equals(runtime.Path, expectedPath, StringComparison.Ordinal)
            || !string.Equals(runtime.MediaType, RuntimeIconRenderer.MediaType, StringComparison.Ordinal)
            || runtime.Width != RuntimeIconRenderer.Width
            || runtime.Height != RuntimeIconRenderer.Height
            || !string.Equals(runtime.RenderProfile, RuntimeIconRenderer.RenderProfile, StringComparison.Ordinal)
            || runtime.Sha256.Length != 64
            || runtime.Sha256.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || !expectedFiles.Add(expectedPath)
            || !files.TryGetValue(expectedPath, out var png))
            throw new InputValidationException($"Version 2 brand '{brand.Id}' has invalid or missing runtime icon metadata.");

        var actualHash = SHA256.HashData(png);
        var declaredHash = Convert.FromHexString(runtime.Sha256);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, declaredHash))
            throw new InputValidationException($"Runtime PNG '{expectedPath}' does not match its SHA-256.");

        using var decoded = DeterministicPng.DecodeAndValidate(
            png, runtime.Width, runtime.Height, expectedPath, brand.BackgroundColor!);
    }

    private static void ValidateAppearances(
        PackBrand brand,
        IReadOnlyDictionary<string, byte[]> files,
        ISet<string> expectedFiles,
        IReadOnlySet<string> sourceProviders,
        ISet<string> validatedRuntimeHashes,
        ISet<string> validatedRuntimeProfiles)
    {
        var appearances = brand.Appearances
            ?? throw new InputValidationException($"Version 3 brand '{brand.Id}' has no appearances.");
        ValidateAppearance(brand.Id, "lightTheme", appearances.LightTheme, files, expectedFiles, sourceProviders,
            validatedRuntimeHashes, validatedRuntimeProfiles);
        ValidateAppearance(brand.Id, "darkTheme", appearances.DarkTheme, files, expectedFiles, sourceProviders,
            validatedRuntimeHashes, validatedRuntimeProfiles);
    }

    private static void ValidateAppearance(
        string brandId,
        string theme,
        ThemeAppearance appearance,
        IReadOnlyDictionary<string, byte[]> files,
        ISet<string> expectedFiles,
        IReadOnlySet<string> sourceProviders,
        ISet<string> validatedRuntimeHashes,
        ISet<string> validatedRuntimeProfiles)
    {
        if (appearance is null)
            throw new InputValidationException($"Version 3 brand '{brandId}' has no {theme} appearance.");
        if (appearance.AssetRole is not ("foreground" or "complete-icon"))
            throw new InputValidationException($"Version 3 brand '{brandId}' has invalid {theme} assetRole '{appearance.AssetRole}'.");
        if (appearance.Background is null
            || appearance.AssetRole == "foreground" && (appearance.Background.Mode != "solid"
                || !IsCanonicalColor(appearance.Background.Color))
            || appearance.AssetRole == "complete-icon" && (appearance.Background.Mode != "embedded"
                || appearance.Background.Color is not null))
            throw new InputValidationException($"Version 3 brand '{brandId}' has invalid {theme} background semantics.");
        if (appearance.Selection is not ("native-theme-variant" or "brand-color-foreground"
                or "complete-source-artwork" or "source-default" or "reviewed-badge-composition")
            || !ProviderHelpers.IsSafeDisplayText(appearance.AppearanceSelectionReason, 512)
            || appearance.Source is null
            || !sourceProviders.Contains(appearance.Source.Provider)
            || !ProviderHelpers.IsSafeDisplayText(appearance.Source.SourceId)
            || !ProviderHelpers.IsSafeDisplayText(appearance.Source.AssetPath, 512))
            throw new InputValidationException($"Version 3 brand '{brandId}' has invalid {theme} selection provenance.");

        var runtime = appearance.Asset
            ?? throw new InputValidationException($"Version 3 brand '{brandId}' has no {theme} asset.");
        var expectedPath = $"runtime/appearance-128/{runtime.Sha256}.png";
        if (runtime.Path != expectedPath
            || runtime.MediaType != RuntimeIconRenderer.MediaType
            || runtime.Width != RuntimeIconRenderer.Width || runtime.Height != RuntimeIconRenderer.Height
            || runtime.RenderProfile != RuntimeIconRenderer.ThemeRenderProfile
            || runtime.Sha256.Length != 64
            || runtime.Sha256.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || !files.TryGetValue(expectedPath, out var png))
            throw new InputValidationException($"Version 3 brand '{brandId}' has invalid or missing {theme} runtime asset metadata.");
        expectedFiles.Add(expectedPath);
        if (validatedRuntimeHashes.Add(expectedPath))
        {
            var actualHash = SHA256.HashData(png);
            var declaredHash = Convert.FromHexString(runtime.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, declaredHash))
                throw new InputValidationException($"Runtime PNG '{expectedPath}' does not match its SHA-256.");
        }
        var background = appearance.AssetRole == "foreground" ? appearance.Background.Color : null;
        var validationProfile = expectedPath + "\0" + background;
        if (validatedRuntimeProfiles.Add(validationProfile))
            using (DeterministicPng.DecodeAndValidate(png, runtime.Width, runtime.Height,
                       $"{expectedPath}' referenced by '{brandId}/{theme}", background)) { }
    }

    private static bool IsCanonicalColor(string? color)
        => color is { Length: 7 } && color[0] == '#'
            && ProviderHelpers.IsHexColor(color[1..])
            && color == color.ToUpperInvariant();

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
