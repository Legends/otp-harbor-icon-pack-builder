namespace OtpHarbor.IconPackBuilder.Resolution;

public sealed record ProviderMatch(string Provider, string SourceId);
public sealed record CanonicalBrandMapping(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ProviderMatch> Matches,
    string? BackgroundColor = null);
public sealed record SourceOverride(string Provider, string SourceId);
public sealed record RightsOverride(
    RightsStatus Status,
    string? LicenseType,
    string? EvidenceUrl,
    string? Note);

public sealed record MappingSet(
    IReadOnlyList<CanonicalBrandMapping> CanonicalBrands,
    IReadOnlyDictionary<string, IReadOnlyList<string>> IssuerAliases,
    IReadOnlyDictionary<string, SourceOverride> SourceOverrides,
    IReadOnlyDictionary<string, RightsOverride>? RightsOverrides = null)
{
    public IReadOnlyDictionary<string, RightsOverride> EffectiveRightsOverrides { get; } =
        RightsOverrides ?? new Dictionary<string, RightsOverride>(StringComparer.OrdinalIgnoreCase);

    public static MappingSet Empty { get; } = new([], new Dictionary<string, IReadOnlyList<string>>(),
        new Dictionary<string, SourceOverride>(), new Dictionary<string, RightsOverride>(StringComparer.OrdinalIgnoreCase));
}
