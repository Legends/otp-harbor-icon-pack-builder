using System.Text.Json.Serialization;

namespace OtpHarbor.IconPackBuilder.Domain;

public sealed record IconSourceInput(
    string Provider,
    string ArchivePath,
    string? Version = null,
    string? Revision = null,
    string? SourceUrl = null);

public sealed record SourceLicense(string FileName, byte[] Content);

public sealed record SourceRecord(
    string Provider,
    string SourceId,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    string AssetPath,
    byte[] Svg,
    string Quality,
    IReadOnlyDictionary<string, string?> Metadata,
    bool AutomaticCandidate = true,
    IReadOnlyList<SourceAssetVariant>? Variants = null);

public sealed record SourceAssetVariant(
    string SourceId,
    string AssetPath,
    byte[] Svg,
    string Kind);

public sealed record ProviderCatalog(
    string Provider,
    string InputFileName,
    string InputSha256,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<SourceRecord> Records,
    IReadOnlyList<SourceLicense> Licenses,
    int SkippedRecords = 0,
    string? Version = null,
    string? Revision = null,
    string? SourceUrl = null);

public sealed class PackDocument
{
    public int FormatVersion { get; init; }
    public required string PackId { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<PackSource> Sources { get; init; }
    public required IReadOnlyList<PackBrand> Brands { get; init; }
    public required IReadOnlyList<IssuerAliasEntry> IssuerAliases { get; init; }
}

public sealed record PackSource(
    string Provider,
    string InputFileName,
    string Sha256,
    string? Version,
    string? Revision,
    string? SourceUrl,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<string> LicenseFiles);

public sealed record PackBrand(
    string Id,
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BackgroundColor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Icon,
    IReadOnlyList<string> IssuerAliases,
    SelectedSource SelectedSource,
    IReadOnlyList<SourceReference> Sources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    RuntimeIcon? RuntimeIcon = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ThemeAppearances? Appearances = null);

public sealed record ThemeAppearances(
    ThemeAppearance LightTheme,
    ThemeAppearance DarkTheme);

public sealed record ThemeAppearance(
    RuntimeIcon Asset,
    string AssetRole,
    AppearanceBackground Background,
    string Selection,
    AppearanceSource Source,
    string AppearanceSelectionReason);

public sealed record AppearanceBackground(
    string Mode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Color = null);

public sealed record AppearanceSource(
    string Provider,
    string SourceId,
    string AssetPath);

public sealed record RuntimeIcon(
    string Path,
    string MediaType,
    int Width,
    int Height,
    string Sha256,
    string RenderProfile);

public sealed record SelectedSource(
    string Provider,
    string SourceId,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record SourceReference(
    string Provider,
    string SourceId,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record IssuerAliasEntry(string Key, string BrandId);

public sealed record PackBuildResult(
    PackDocument Document,
    IReadOnlyDictionary<string, byte[]> Icons,
    IReadOnlyDictionary<string, byte[]> Licenses,
    BuildSummary Summary,
    IReadOnlyList<VisualVerificationEntry> VisualVerification,
    IReadOnlyList<RightsAssessmentEntry>? RightsAssessments = null,
    IReadOnlyDictionary<string, byte[]>? RuntimeIcons = null,
    IReadOnlyList<RuntimeVisualReference>? RuntimeVisualReferences = null);

public sealed record RuntimeVisualReference(
    string BrandId,
    string Appearance,
    string AssetPath,
    string ExpectedSha256,
    byte[] CanonicalSvg,
    string PreviewBackgroundColor,
    string? CompositionMode = null,
    string? CompositionColor = null,
    byte[]? SourceSvg = null);

public enum RightsStatus
{
    Unknown,
    Documented,
    AttributionRequired,
    Restricted
}

public enum RightsPolicy
{
    Preserve,
    DocumentedOnly,
    RequireDocumented
}

public sealed record RightsAssessment(
    RightsStatus Status,
    string Basis,
    string? LicenseType,
    string? EvidenceUrl,
    string? Note);

public sealed record RightsAssessmentEntry(
    string BrandId,
    string Provider,
    string SourceId,
    RightsStatus Status,
    string Basis,
    string? LicenseType,
    string? EvidenceUrl,
    string? Note);

public sealed record VisualVerificationEntry(
    string BrandId,
    string Provider,
    string SourceId,
    string SourceAssetPath,
    string BackgroundColor,
    IReadOnlyList<string> Operations,
    double MeanAbsoluteChannelDifference,
    int MateriallyDifferentPixels,
    int TotalPixels,
    IReadOnlyList<string> SourcePalette,
    IReadOnlyList<string> NormalizedPalette,
    bool Resolved,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Appearance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AssetRole = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AppearanceSelectionReason = null);

public sealed record BuildSummary(
    IReadOnlyDictionary<string, int> RecordsByProvider,
    int CanonicalBrands,
    int Aliases,
    int BrandsMerged,
    int Conflicts,
    int SuppressedAliases,
    int SkippedRecords,
    IReadOnlyDictionary<string, int> SelectedIconsByProvider,
    IReadOnlyDictionary<string, int>? SelectedRightsByStatus = null,
    int RightsExcludedRecords = 0);

public sealed record BuildConflict(string Type, string Key, string Message, IReadOnlyList<string> Claims);

public sealed class BuildConflictException : Exception
{
    public BuildConflictException(IReadOnlyList<BuildConflict> conflicts)
        : base($"Build failed with {conflicts.Count} conflict(s).") => Conflicts = conflicts;

    public IReadOnlyList<BuildConflict> Conflicts { get; }
}

public sealed class InputValidationException(string message) : Exception(message);
