using System.Text.Json;
using OtpHarbor.IconPackBuilder.Domain;

namespace OtpHarbor.IconPackBuilder.Providers.Aegis;

public sealed class AegisProvider(ArchiveLimits? limits = null) : IIconSourceProvider
{
    private readonly ArchiveLimits _limits = limits ?? new ArchiveLimits();
    public string Id => "aegis";

    public async Task<ProviderCatalog> LoadAsync(IconSourceInput input, CancellationToken cancellationToken = default)
    {
        using var archive = SecureZipArchive.Open(input.ArchivePath, _limits);
        var metadataPath = archive.FindRequired("pack.json");
        using var json = ProviderHelpers.ParseJson(await archive.ReadTextAsync(metadataPath, cancellationToken), Id, metadataPath);
        if (json.RootElement.ValueKind != JsonValueKind.Object
            || !json.RootElement.TryGetProperty("uuid", out var uuid)
            || uuid.ValueKind != JsonValueKind.String
            || !Guid.TryParse(uuid.GetString(), out _)
            || !json.RootElement.TryGetProperty("name", out var packName)
            || packName.ValueKind != JsonValueKind.String
            || !ProviderHelpers.IsSafeDisplayText(packName.GetString())
            || !json.RootElement.TryGetProperty("version", out var version)
            || version.ValueKind is not (JsonValueKind.Number or JsonValueKind.String)
            || version.ValueKind == JsonValueKind.String && !ProviderHelpers.IsSafeDisplayText(version.GetString(), 64)
            || !json.RootElement.TryGetProperty("icons", out var icons)
            || icons.ValueKind != JsonValueKind.Array)
            throw new InputValidationException("aegis: pack.json requires a valid UUID, name, version, and icons array.");

        var packMetadata = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["name"] = OptionalScalar(json.RootElement, "name"),
            ["uuid"] = OptionalScalar(json.RootElement, "uuid"),
            ["version"] = OptionalScalar(json.RootElement, "version")
        };
        var records = new List<SourceRecord>();
        var skipped = 0;
        var index = 0;
        foreach (var icon in icons.EnumerateArray())
        {
            index++;
            if (icon.ValueKind != JsonValueKind.Object)
                throw new InputValidationException($"aegis: icon record #{index} must be an object.");
            var fileName = RequiredString(icon, "filename", index);
            var sourceId = Path.GetFileNameWithoutExtension(fileName);
            if (!ProviderHelpers.IsSafeDisplayText(sourceId, 256))
                throw new InputValidationException($"aegis: icon record #{index} has an invalid filename stem.");
            var name = icon.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && ProviderHelpers.IsSafeDisplayText(nameElement.GetString())
                    ? nameElement.GetString()!
                    : sourceId;
            if (IsGeneric(fileName, icon))
            {
                skipped++;
                continue;
            }
            if (!fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            var assetPath = archive.ResolveRelative(metadataPath, fileName);
            var svg = await archive.ReadAsync(assetPath, _limits.MaxSvgBytes, cancellationToken);
            var backgroundColor = ProviderHelpers.ValidateSvg(svg, Id, assetPath, _limits.MaxSvgBytes);
            var aliases = new List<string> { name, sourceId };
            if (icon.TryGetProperty("issuer", out var issuer))
            {
                if (issuer.ValueKind != JsonValueKind.Array)
                    throw new InputValidationException($"aegis: 'issuer' for '{name}' must be an array.");
                foreach (var alias in issuer.EnumerateArray())
                    if (alias.ValueKind == JsonValueKind.String && ProviderHelpers.IsSafeDisplayText(alias.GetString(), 128))
                        aliases.Add(alias.GetString()!);
            }
            var normalizedFileName = fileName.Replace('\\', '/');
            var isVariation = normalizedFileName.StartsWith("2_Variations/", StringComparison.OrdinalIgnoreCase)
                || normalizedFileName.Contains("/2_Variations/", StringComparison.OrdinalIgnoreCase);
            records.Add(new SourceRecord(Id, sourceId, name, aliases, assetPath, svg, isVariation ? "variation" : "primary",
                new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["assetPath"] = fileName,
                    ["extractedBrandColor"] = backgroundColor,
                    ["category"] = OptionalScalar(icon, "category")
                }, AutomaticCandidate: !isVariation));
            if (records.Count > 5_000)
                throw new InputValidationException("aegis: pack contains more than 5,000 usable SVG records.");
        }

        return new ProviderCatalog(Id, Path.GetFileName(input.ArchivePath),
            await SecureZipArchive.Sha256Async(input.ArchivePath, cancellationToken), packMetadata,
            records, ProviderHelpers.ReadLicenses(archive, cancellationToken), skipped,
            input.Version ?? packMetadata["version"], input.Revision, input.SourceUrl);
    }

    private static bool IsGeneric(string path, JsonElement icon)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("3_Categories/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/3_Categories/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("3_Generic/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/3_Generic/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("Generic/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/Generic/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (icon.TryGetProperty("category", out var category) && category.ValueKind == JsonValueKind.String)
        {
            var value = category.GetString();
            return value is not null && (value.Equals("Generic", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Categories", StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    private static string RequiredString(JsonElement element, string property, int index)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && ProviderHelpers.IsSafeDisplayText(value.GetString(), property == "filename" ? 512 : 256)
            ? value.GetString()!
            : throw new InputValidationException($"aegis: icon record #{index} requires non-empty '{property}'.");

    private static string? OptionalScalar(JsonElement element, string property)
        => !element.TryGetProperty(property, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null : value.ToString();
}
