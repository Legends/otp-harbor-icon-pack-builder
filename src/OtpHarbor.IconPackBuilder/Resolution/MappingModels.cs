namespace OtpHarbor.IconPackBuilder.Resolution;

public sealed record ProviderMatch(string Provider, string SourceId);
public sealed record CanonicalBrandMapping(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<ProviderMatch> Matches,
    string? BackgroundColor = null);
public sealed record SourceOverride(
    string Provider,
    string SourceId,
    string? LightForegroundColor = null,
    string? DarkForegroundColor = null,
    string? AppearanceRationale = null);
public sealed record AppearanceCompositionOverride(
    string Provider,
    string SourceId,
    string Mode,
    string MatteColor,
    string Rationale);
public sealed record RightsOverride(
    RightsStatus Status,
    string? LicenseType,
    string? EvidenceUrl,
    string? Note);

public sealed record MappingSet(
    IReadOnlyList<CanonicalBrandMapping> CanonicalBrands,
    IReadOnlyDictionary<string, IReadOnlyList<string>> IssuerAliases,
    IReadOnlyDictionary<string, SourceOverride> SourceOverrides,
    IReadOnlyDictionary<string, RightsOverride>? RightsOverrides = null,
    IReadOnlyDictionary<string, AppearanceCompositionOverride>? AppearanceCompositions = null)
{
    public IReadOnlyDictionary<string, RightsOverride> EffectiveRightsOverrides { get; } =
        RightsOverrides ?? new Dictionary<string, RightsOverride>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, AppearanceCompositionOverride> EffectiveAppearanceCompositions { get; } =
        AppearanceCompositions ?? new Dictionary<string, AppearanceCompositionOverride>(StringComparer.Ordinal);

    public static MappingSet Empty { get; } = new([], new Dictionary<string, IReadOnlyList<string>>(),
        new Dictionary<string, SourceOverride>(), new Dictionary<string, RightsOverride>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, AppearanceCompositionOverride>(StringComparer.Ordinal));
}
