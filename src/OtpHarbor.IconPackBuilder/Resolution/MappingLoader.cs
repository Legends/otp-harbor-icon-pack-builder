using System.Text.Json;
using OtpHarbor.IconPackBuilder.Normalization;
using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Resolution;

public static class MappingLoader
{
    private const string EmbeddedPrefix = "OtpHarbor.IconPackBuilder.Mappings.";

    public static async Task<MappingSet> LoadAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
            throw new InputValidationException($"Mapping directory does not exist: {directory}");
        var canonicalPath = Path.Combine(directory, "canonical-brands.json");
        var aliasesPath = Path.Combine(directory, "issuer-aliases.json");
        var overridesPath = Path.Combine(directory, "source-overrides.json");
        var rightsPath = Path.Combine(directory, "rights-assessments.json");
        var canonical = await ReadAsync(canonicalPath, cancellationToken);
        var aliases = await ReadAsync(aliasesPath, cancellationToken);
        var overrides = await ReadAsync(overridesPath, cancellationToken);
        var rights = File.Exists(rightsPath)
            ? ParseRights(await ReadAsync(rightsPath, cancellationToken), rightsPath)
            : new Dictionary<string, RightsOverride>(StringComparer.OrdinalIgnoreCase);
        return new MappingSet(ParseCanonical(canonical, canonicalPath), ParseAliases(aliases, aliasesPath),
            ParseOverrides(overrides, overridesPath), rights);
    }

    public static async Task<MappingSet> LoadDefaultAsync(CancellationToken cancellationToken = default)
    {
        var canonical = await ReadEmbeddedAsync("canonical-brands.json", cancellationToken);
        var aliases = await ReadEmbeddedAsync("issuer-aliases.json", cancellationToken);
        var overrides = await ReadEmbeddedAsync("source-overrides.json", cancellationToken);
        var rights = await ReadEmbeddedAsync("rights-assessments.json", cancellationToken);
        return new MappingSet(
            ParseCanonical(canonical, "embedded:canonical-brands.json"),
            ParseAliases(aliases, "embedded:issuer-aliases.json"),
            ParseOverrides(overrides, "embedded:source-overrides.json"),
            ParseRights(rights, "embedded:rights-assessments.json"));
    }

    private static async Task<JsonDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new InputValidationException($"Required mapping file does not exist: {path}");
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow }, cancellationToken);
        }
        catch (JsonException ex) { throw new InputValidationException($"Malformed mapping JSON '{path}': {ex.Message}"); }
    }

    private static async Task<JsonDocument> ReadEmbeddedAsync(string fileName, CancellationToken cancellationToken)
    {
        var resourceName = EmbeddedPrefix + fileName;
        await using var stream = typeof(MappingLoader).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InputValidationException($"Required embedded mapping does not exist: {fileName}");
        try
        {
            return await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Disallow },
                cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InputValidationException($"Malformed embedded mapping '{fileName}': {ex.Message}");
        }
    }

    private static IReadOnlyList<CanonicalBrandMapping> ParseCanonical(JsonDocument document, string path)
    {
        using (document)
        {
            var root = document.RootElement;
            EnsureVersion(root, path);
            if (!root.TryGetProperty("brands", out var brands) || brands.ValueKind != JsonValueKind.Array)
                throw new InputValidationException($"Mapping '{path}' requires a 'brands' array.");
            var result = new List<CanonicalBrandMapping>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var sourceClaims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var brand in brands.EnumerateArray())
            {
                var id = RequiredString(brand, "id", path);
                if (CanonicalId.FromName(id) != id)
                    throw new InputValidationException($"Canonical mapping ID '{id}' must be lowercase kebab-case.");
                if (!ids.Add(id)) throw new InputValidationException($"Duplicate canonical mapping ID '{id}'.");
                var displayName = RequiredString(brand, "displayName", path);
                var backgroundColor = OptionalString(brand, "backgroundColor");
                if (backgroundColor is not null
                    && (backgroundColor.Length != 7 || backgroundColor[0] != '#'
                        || !ProviderHelpers.IsHexColor(backgroundColor[1..])))
                    throw new InputValidationException($"Canonical mapping '{id}' has invalid backgroundColor '{backgroundColor}'.");
                var brandAliases = ReadStringArray(brand, "aliases", path);
                var matches = new List<ProviderMatch>();
                if (brand.TryGetProperty("matches", out var matchArray))
                {
                    if (matchArray.ValueKind != JsonValueKind.Array) throw new InputValidationException($"matches for '{id}' must be an array.");
                    foreach (var match in matchArray.EnumerateArray())
                    {
                        var provider = RequiredString(match, "provider", path);
                        var sourceId = RequiredString(match, "sourceId", path);
                        if (!sourceClaims.Add(provider + "\0" + sourceId))
                            throw new InputValidationException($"Provider record '{provider}/{sourceId}' has multiple canonical mappings.");
                        matches.Add(new ProviderMatch(provider, sourceId));
                    }
                }
                result.Add(new CanonicalBrandMapping(id, displayName, brandAliases, matches, backgroundColor?.ToUpperInvariant()));
            }
            return result.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ParseAliases(JsonDocument document, string path)
    {
        using (document)
        {
            var root = document.RootElement;
            EnsureVersion(root, path);
            if (!root.TryGetProperty("aliases", out var aliases) || aliases.ValueKind != JsonValueKind.Object)
                throw new InputValidationException($"Mapping '{path}' requires an 'aliases' object.");
            return aliases.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)
                .ToDictionary(x => x.Name, x => (IReadOnlyList<string>)ReadStringArrayValue(x.Value, path), StringComparer.Ordinal);
        }
    }

    private static IReadOnlyDictionary<string, SourceOverride> ParseOverrides(JsonDocument document, string path)
    {
        using (document)
        {
            var root = document.RootElement;
            EnsureVersion(root, path);
            if (!root.TryGetProperty("overrides", out var overrides) || overrides.ValueKind != JsonValueKind.Object)
                throw new InputValidationException($"Mapping '{path}' requires an 'overrides' object.");
            return overrides.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)
                .ToDictionary(x => x.Name, x => new SourceOverride(RequiredString(x.Value, "provider", path), RequiredString(x.Value, "sourceId", path)), StringComparer.Ordinal);
        }
    }

    private static IReadOnlyDictionary<string, RightsOverride> ParseRights(JsonDocument document, string path)
    {
        using (document)
        {
            var root = document.RootElement;
            EnsureVersion(root, path);
            if (!root.TryGetProperty("assessments", out var assessments) || assessments.ValueKind != JsonValueKind.Object)
                throw new InputValidationException($"Mapping '{path}' requires an 'assessments' object.");
            var result = new Dictionary<string, RightsOverride>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in assessments.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var separator = property.Name.IndexOf('/');
                if (separator <= 0 || separator == property.Name.Length - 1)
                    throw new InputValidationException($"Rights assessment key '{property.Name}' must be 'provider/sourceId'.");
                var statusValue = RequiredString(property.Value, "status", path);
                if (!TryParseRightsStatus(statusValue, out var status))
                    throw new InputValidationException($"Rights assessment '{property.Name}' has unsupported status '{statusValue}'.");
                var evidenceUrl = OptionalString(property.Value, "evidenceUrl");
                if (evidenceUrl is not null
                    && (!Uri.TryCreate(evidenceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                    throw new InputValidationException($"Rights assessment '{property.Name}' requires an HTTPS evidenceUrl.");
                result.Add(property.Name, new RightsOverride(status, OptionalString(property.Value, "licenseType"),
                    evidenceUrl, OptionalString(property.Value, "note")));
            }
            return result;
        }
    }

    private static bool TryParseRightsStatus(string value, out RightsStatus status)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal);
        return Enum.TryParse(normalized, ignoreCase: true, out status);
    }

    private static void EnsureVersion(JsonElement root, string path)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber) || versionNumber != 1)
            throw new InputValidationException($"Mapping '{path}' must have version 1.");
    }

    private static string RequiredString(JsonElement element, string property, string path)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new InputValidationException($"Mapping '{path}' requires non-empty '{property}'.");

    private static string? OptionalString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property, string path)
        => !element.TryGetProperty(property, out var array) ? [] : ReadStringArrayValue(array, path);

    private static string[] ReadStringArrayValue(JsonElement array, string path)
    {
        if (array.ValueKind != JsonValueKind.Array || array.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(x.GetString())))
            throw new InputValidationException($"Mapping '{path}' contains an invalid string array.");
        return array.EnumerateArray().Select(x => x.GetString()!).ToArray();
    }
}
