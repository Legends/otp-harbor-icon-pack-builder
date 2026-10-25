using System.Text.Json;
using OtpHarbor.IconPackBuilder.Domain;

namespace OtpHarbor.IconPackBuilder.Packaging;

public static class RightsAssessmentReportWriter
{
    public static async Task WriteAsync(
        string path,
        PackDocument document,
        RightsPolicy policy,
        IReadOnlyList<RightsAssessmentEntry> assessments,
        int excludedRecords,
        CancellationToken cancellationToken = default)
    {
        var formatVersion = document.FormatVersion;
        if (formatVersion is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        var ordered = assessments.OrderBy(x => x.BrandId, StringComparer.Ordinal).ToArray();
        var expectedIds = document.Brands.Select(brand => brand.Id).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var actualIds = ordered.Select(entry => entry.BrandId).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
            throw new InputValidationException("Rights assessment report coverage does not exactly match the manifest brands.");
        var counts = ordered.GroupBy(x => RightsStatusValue(x.Status), StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        var report = new
        {
            formatVersion,
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
