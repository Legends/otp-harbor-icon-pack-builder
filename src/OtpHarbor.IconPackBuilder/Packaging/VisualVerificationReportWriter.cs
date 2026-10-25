using System.Text.Json;
using OtpHarbor.IconPackBuilder.Domain;

namespace OtpHarbor.IconPackBuilder.Packaging;

public static class VisualVerificationReportWriter
{
    public static async Task WriteAsync(
        string path,
        PackDocument document,
        IReadOnlyList<VisualVerificationEntry> entries,
        CancellationToken cancellationToken = default)
    {
        var formatVersion = document.FormatVersion;
        if (formatVersion is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        var ordered = entries.OrderBy(x => x.BrandId, StringComparer.Ordinal).ToArray();
        ValidateCoverage(document.Brands.Select(brand => brand.Id), ordered.Select(entry => entry.BrandId), "Visual verification");
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

    private static void ValidateCoverage(IEnumerable<string> expected, IEnumerable<string> actual, string description)
    {
        var expectedIds = expected.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var actualIds = actual.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
            throw new InputValidationException($"{description} report coverage does not exactly match the manifest brands.");
    }
}
