using System.Text.Json;
using OtpHarbor.IconPackBuilder.Domain;

namespace OtpHarbor.IconPackBuilder.Providers.SimpleIcons;

public sealed class SimpleIconsProvider(ArchiveLimits? limits = null) : IIconSourceProvider
{
    private readonly ArchiveLimits _limits = limits ?? new ArchiveLimits();
    public string Id => "simple-icons";

    public async Task<ProviderCatalog> LoadAsync(IconSourceInput input, CancellationToken cancellationToken = default)
    {
        using var archive = SecureZipArchive.Open(input.ArchivePath, _limits);
        var metadataPath = archive.FindRequired("data/simple-icons.json");
        using var json = ProviderHelpers.ParseJson(await archive.ReadTextAsync(metadataPath, cancellationToken), Id, metadataPath);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InputValidationException("simple-icons: data/simple-icons.json must contain an array.");

        var records = new List<SourceRecord>();
        var index = 0;
        foreach (var icon in json.RootElement.EnumerateArray())
        {
            index++;
            if (icon.ValueKind != JsonValueKind.Object)
                throw new InputValidationException($"simple-icons: record #{index} must be an object.");
            var title = RequiredString(icon, "title", index);
            var slug = icon.TryGetProperty("slug", out var slugElement) && slugElement.ValueKind == JsonValueKind.String
                ? slugElement.GetString()! : ProviderHelpers.SimpleSlug(title);
            if (string.IsNullOrWhiteSpace(slug))
                throw new InputValidationException($"simple-icons: record '{title}' has no usable slug.");
            var hex = OptionalString(icon, "hex");
            if (hex is not null && !ProviderHelpers.IsHexColor(hex))
                throw new InputValidationException($"simple-icons: record '{title}' has invalid brand color '{hex}'.");
            var assetPath = archive.ResolveRelative(metadataPath, $"icons/{slug}.svg");
            var svg = await archive.ReadAsync(assetPath, _limits.MaxSvgBytes, cancellationToken);
            var extractedColor = ProviderHelpers.ValidateSvg(svg, Id, assetPath, _limits.MaxSvgBytes);

            var aliases = new List<string> { title };
            if (icon.TryGetProperty("aliases", out var aliasesObject) && aliasesObject.ValueKind == JsonValueKind.Object)
            {
                AddStringArray(aliasesObject, "aka", aliases, title);
                AddStringArray(aliasesObject, "old", aliases, title);
                if (aliasesObject.TryGetProperty("loc", out var localized) && localized.ValueKind == JsonValueKind.Object)
                    aliases.AddRange(localized.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.String).Select(x => x.Value.GetString()!));
            }

            string? licenseType = null;
            string? licenseUrl = null;
            if (icon.TryGetProperty("license", out var license) && license.ValueKind == JsonValueKind.Object)
            {
                licenseType = OptionalString(license, "type");
                licenseUrl = OptionalString(license, "url");
            }
            records.Add(new SourceRecord(Id, slug, title, aliases, assetPath, svg, "default",
                new SortedDictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["assetPath"] = $"icons/{slug}.svg",
                    ["extractedBrandColor"] = extractedColor,
                    ["guidelinesUrl"] = OptionalString(icon, "guidelines"),
                    ["licenseType"] = licenseType,
                    ["licenseUrl"] = licenseUrl,
                    ["nativeBrandColor"] = hex is not null ? $"#{hex.ToUpperInvariant()}" : null,
                    ["sourceUrl"] = OptionalString(icon, "source")
                }));
            if (records.Count > 5_000)
                throw new InputValidationException("simple-icons: metadata contains more than 5,000 icon records.");
        }

        var metadata = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        const string metadataSuffix = "data/simple-icons.json";
        var prefix = metadataPath[..^metadataSuffix.Length];
        var expectedPackagePath = prefix + "package.json";
        var packagePath = archive.Paths.SingleOrDefault(path =>
            path.Equals(expectedPackagePath, StringComparison.OrdinalIgnoreCase));
        if (packagePath is not null)
        {
            using var package = ProviderHelpers.ParseJson(await archive.ReadTextAsync(packagePath, cancellationToken), Id, packagePath);
            if (package.RootElement.ValueKind == JsonValueKind.Object)
                metadata["version"] = OptionalString(package.RootElement, "version");
        }
        return new ProviderCatalog(Id, Path.GetFileName(input.ArchivePath),
            await SecureZipArchive.Sha256Async(input.ArchivePath, cancellationToken), metadata, records,
            ProviderHelpers.ReadLicenses(archive, cancellationToken), Version: input.Version ?? metadata.GetValueOrDefault("version"),
            Revision: input.Revision, SourceUrl: input.SourceUrl);
    }

    private static void AddStringArray(JsonElement parent, string property, List<string> output, string title)
    {
        if (!parent.TryGetProperty(property, out var values)) return;
        if (values.ValueKind != JsonValueKind.Array)
            throw new InputValidationException($"simple-icons: aliases.{property} for '{title}' must be an array.");
        foreach (var alias in values.EnumerateArray())
        {
            var value = alias.ValueKind == JsonValueKind.String
                ? alias.GetString()
                : alias.ValueKind == JsonValueKind.Object
                    && alias.TryGetProperty("title", out var titleElement)
                    && titleElement.ValueKind == JsonValueKind.String
                        ? titleElement.GetString()
                        : null;
            if (ProviderHelpers.IsSafeDisplayText(value, 128)) output.Add(value!);
        }
    }

    private static string RequiredString(JsonElement element, string property, int index)
        => OptionalString(element, property) is { } value && ProviderHelpers.IsSafeDisplayText(value) ? value
            : throw new InputValidationException($"simple-icons: record #{index} requires non-empty '{property}'.");

    private static string? OptionalString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
