namespace OtpHarbor.IconPackBuilder.Resolution;

public static class RightsAssessor
{
    public static RightsAssessment Assess(
        SourceRecord record,
        IReadOnlyDictionary<string, RightsOverride> overrides)
    {
        if (overrides.TryGetValue(Key(record.Provider, record.SourceId), out var value))
            return new RightsAssessment(value.Status, "manual-review", value.LicenseType,
                value.EvidenceUrl, value.Note);

        var license = Metadata(record, "licenseType") ?? Metadata(record, "license");
        var evidence = Metadata(record, "licenseUrl");
        if (string.IsNullOrWhiteSpace(license))
            return new RightsAssessment(RightsStatus.Unknown, "no-asset-level-evidence", null,
                evidence, "No asset-level license evidence was supplied by the upstream metadata.");

        var status = RequiresAttribution(license)
            ? RightsStatus.AttributionRequired
            : RightsStatus.Documented;
        return new RightsAssessment(status, "upstream-asset-metadata", license, evidence,
            "Upstream metadata was preserved; this classification is evidence tracking, not legal clearance.");
    }

    public static bool IsDocumented(RightsAssessment assessment)
        => assessment.Status is RightsStatus.Documented or RightsStatus.AttributionRequired;

    public static string ToMetadataValue(RightsStatus status) => status switch
    {
        RightsStatus.AttributionRequired => "attribution-required",
        _ => status.ToString().ToLowerInvariant()
    };

    public static string Key(string provider, string sourceId) => provider + "/" + sourceId;

    private static string? Metadata(SourceRecord record, string name)
        => record.Metadata.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static bool RequiresAttribution(string license)
    {
        var normalized = license.Replace('_', '-').Replace(' ', '-').ToUpperInvariant();
        if (normalized is "CUSTOM" or "OTHER" or "UNKNOWN") return false;
        return !normalized.StartsWith("CC0", StringComparison.Ordinal)
            && !normalized.Contains("PUBLIC-DOMAIN", StringComparison.Ordinal);
    }
}
