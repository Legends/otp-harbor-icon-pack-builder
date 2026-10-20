using System.Text.Json;
using OtpHarbor.IconPackBuilder.Domain;

namespace OtpHarbor.IconPackBuilder.Packaging;

public static class VisualVerificationReportWriter
{
    public static async Task WriteAsync(
        string path,
        int formatVersion,
        IReadOnlyList<VisualVerificationEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (formatVersion is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        var ordered = entries.OrderBy(x => x.BrandId, StringComparer.Ordinal).ToArray();
        var unresolved = ordered.Count(x => !x.Resolved);
        if (unresolved != 0)
            throw new InputValidationException($"Visual verification has {unresolved} unresolved failure(s); the pack cannot be published.");

        var report = new
        {
            formatVersion,
            totalBrands = ordered.Length,
            unresolvedFailures = unresolved,
            brands = ordered
        };
        await File.WriteAllBytesAsync(path,
            JsonSerializer.SerializeToUtf8Bytes(report, PackSerializer.JsonOptions), cancellationToken);
    }
}
