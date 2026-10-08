using System.Text.Json;

namespace OtpHarbor.IconPackBuilder.Packaging;

public static class RightsAssessmentReportWriter
{
    public static async Task WriteAsync(
        string path,
        RightsPolicy policy,
        IReadOnlyList<RightsAssessmentEntry> assessments,
        int excludedRecords,
        CancellationToken cancellationToken = default)
    {
        var ordered = assessments.OrderBy(x => x.BrandId, StringComparer.Ordinal).ToArray();
        var counts = ordered.GroupBy(x => RightsStatusValue(x.Status), StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var report = new
        {
            formatVersion = 1,
            policy = PolicyValue(policy),
            notLegalClearance = true,
            explanation = "Statuses record available evidence. They do not determine copyright permission, trademark compliance, endorsement, or suitability for a particular jurisdiction or use.",
            selectedBrands = ordered.Length,
            excludedSourceRecords = excludedRecords,
            counts,
            brands = ordered.Select(x => new
            {
                x.BrandId,
                x.Provider,
                x.SourceId,
                status = RightsStatusValue(x.Status),
                x.Basis,
                x.LicenseType,
                x.EvidenceUrl,
                x.Note
            })
        };
        await File.WriteAllBytesAsync(path,
            JsonSerializer.SerializeToUtf8Bytes(report, PackSerializer.JsonOptions), cancellationToken);
    }

    public static string PolicyValue(RightsPolicy policy) => policy switch
    {
        RightsPolicy.DocumentedOnly => "documented-only",
        RightsPolicy.RequireDocumented => "require-documented",
        _ => "preserve"
    };

    private static string RightsStatusValue(RightsStatus status) => status switch
    {
        RightsStatus.AttributionRequired => "attribution-required",
        _ => status.ToString().ToLowerInvariant()
    };
}
