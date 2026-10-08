using System.Text.Json;
using OtpHarbor.IconPackBuilder.Acquisition;
using OtpHarbor.IconPackBuilder.Domain;

namespace OtpHarbor.IconPackBuilder.Providers.DashboardIcons;

public sealed class DashboardIconsProvider(ArchiveLimits? limits = null) : IIconSourceProvider
{
    private const string LicenseType = "Apache-2.0";
    private const string LicenseRepositoryUrl = "https://github.com/homarr-labs/dashboard-icons/blob";
    private readonly ArchiveLimits _limits = limits ?? new ArchiveLimits();
    public string Id => "dashboard-icons";

    public async Task<ProviderCatalog> LoadAsync(IconSourceInput input, CancellationToken cancellationToken = default)
    {
        using var archive = SecureZipArchive.Open(input.ArchivePath, _limits);
        var metadataPath = archive.FindRequired("metadata.json");
        using var json = ProviderHelpers.ParseJson(await archive.ReadTextAsync(metadataPath, cancellationToken), Id, metadataPath);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new InputValidationException("dashboard-icons: metadata.json must contain an object keyed by slug.");

        var records = new List<SourceRecord>();
        var skipped = 0;
        var licenseRevision = string.IsNullOrWhiteSpace(input.Revision) ? "main" : input.Revision;
        var catalogMetadata = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["licenseType"] = LicenseType,
            ["licenseUrl"] = $"{LicenseRepositoryUrl}/{licenseRevision}/LICENSE"
        };
        foreach (var property in json.RootElement.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            if (property.Name.Equals(DashboardIconsUpstreamDefinition.AcquisitionMetadataProperty, StringComparison.Ordinal))
            {
                var acquisitionSkipped = ReadAcquisitionSkippedCount(property.Value);
                skipped += acquisitionSkipped;
                catalogMetadata["acquisitionSkippedRecords"] = acquisitionSkipped.ToString(System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }
            var slug = property.Name;
            if (!ProviderHelpers.IsSafeDisplayText(slug, 256))
                throw new InputValidationException("dashboard-icons: metadata contains an invalid slug.");
            var item = property.Value;
            if (item.ValueKind != JsonValueKind.Object)
                throw new InputValidationException($"dashboard-icons: record '{slug}' must be an object.");
            var baseFormat = OptionalString(item, "base");
            var relativeAsset = $"svg/{slug}.svg";
            string assetPath;
            try { assetPath = archive.ResolveRelative(metadataPath, relativeAsset); }
            catch (InputValidationException) when (!string.Equals(baseFormat, "svg", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }
            var svg = await archive.ReadAsync(assetPath, _limits.MaxSvgBytes, cancellationToken);
            var backgroundColor = ProviderHelpers.ValidateSvg(svg, Id, assetPath, _limits.MaxSvgBytes);
            var aliases = new List<string> { ProviderHelpers.HumanizeSlug(slug) };
            if (item.TryGetProperty("aliases", out var aliasArray))
            {
                if (aliasArray.ValueKind != JsonValueKind.Array)
                    throw new InputValidationException($"dashboard-icons: aliases for '{slug}' must be an array.");
                aliases.AddRange(aliasArray.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
            }
            string? light = null;
            string? dark = null;
            if (item.TryGetProperty("colors", out var colors) && colors.ValueKind == JsonValueKind.Object)
            {
                light = OptionalString(colors, "light");
                dark = OptionalString(colors, "dark");
            }
            string? updatedAt = null;
            string? author = null;
            if (item.TryGetProperty("update", out var update) && update.ValueKind == JsonValueKind.Object)
            {
                updatedAt = OptionalString(update, "timestamp");
                if (update.TryGetProperty("author", out var authorObject) && authorObject.ValueKind == JsonValueKind.Object)
                    author = OptionalString(authorObject, "login") ?? OptionalString(authorObject, "name")
                        ?? (authorObject.TryGetProperty("id", out var id) ? id.ToString() : null);
            }
            var categories = item.TryGetProperty("categories", out var categoryArray) && categoryArray.ValueKind == JsonValueKind.Array
                ? string.Join(",", categoryArray.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!).OrderBy(x => x, StringComparer.Ordinal))
                : null;
            var nativeColor = OptionalString(item, "hex") ?? OptionalString(item, "brandColor");
            if (nativeColor is not null)
            {
                nativeColor = nativeColor.TrimStart('#');
                if (!ProviderHelpers.IsHexColor(nativeColor))
                    throw new InputValidationException($"dashboard-icons: record '{slug}' has invalid native color metadata.");
                nativeColor = $"#{nativeColor.ToUpperInvariant()}";
            }
            records.Add(new SourceRecord(Id, slug, ProviderHelpers.HumanizeSlug(slug), aliases, assetPath, svg, "default",
                new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["assetPath"] = relativeAsset,
                    ["author"] = author,
                    ["baseFormat"] = baseFormat,
                    ["categories"] = categories,
                    ["darkVariant"] = dark,
                    ["lightVariant"] = light,
                    ["license"] = OptionalString(item, "license"),
                    ["extractedBrandColor"] = backgroundColor,
                    ["nativeBrandColor"] = nativeColor,
                    ["sourceUrl"] = OptionalString(item, "source"),
                    ["updatedAt"] = updatedAt
                }));
            if (records.Count > 5_000)
                throw new InputValidationException("dashboard-icons: metadata contains more than 5,000 usable SVG records.");
        }

        return new ProviderCatalog(Id, Path.GetFileName(input.ArchivePath),
            await SecureZipArchive.Sha256Async(input.ArchivePath, cancellationToken),
            catalogMetadata, records,
            ProviderHelpers.ReadLicenses(archive, cancellationToken), skipped,
            input.Version, input.Revision, input.SourceUrl);
    }

    private static int ReadAcquisitionSkippedCount(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("skippedRecords", out var records)
            || records.ValueKind != JsonValueKind.Array
            || records.GetArrayLength() > 5_000)
            throw new InputValidationException("dashboard-icons: acquisition metadata is malformed.");
        foreach (var record in records.EnumerateArray())
        {
            if (record.ValueKind != JsonValueKind.Object
                || !record.TryGetProperty("sourceId", out var sourceId)
                || sourceId.ValueKind != JsonValueKind.String
                || !ProviderHelpers.IsSafeDisplayText(sourceId.GetString(), 256)
                || !record.TryGetProperty("reason", out var reason)
                || reason.ValueKind != JsonValueKind.String
                || !ProviderHelpers.IsSafeDisplayText(reason.GetString(), 512))
                throw new InputValidationException("dashboard-icons: acquisition metadata contains an invalid skipped record.");
        }
        return records.GetArrayLength();
    }

    private static string? OptionalString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
