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
        if (formatVersion is not (1 or 2 or 3))
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        var ordered = entries.OrderBy(x => x.BrandId, StringComparer.Ordinal)
            .ThenBy(x => x.Appearance, StringComparer.Ordinal).ToArray();
        if (formatVersion == 3)
            ValidateCoverage(
                document.Brands.SelectMany(brand => new[] { $"{brand.Id}/lightTheme", $"{brand.Id}/darkTheme" }),
                ordered.Select(entry => $"{entry.BrandId}/{entry.Appearance}"),
                "Visual verification");
        else
            ValidateCoverage(document.Brands.Select(brand => brand.Id), ordered.Select(entry => entry.BrandId), "Visual verification");
        var unresolved = ordered.Count(x => !x.Resolved);
        if (unresolved != 0)
            throw new InputValidationException($"Visual verification has {unresolved} unresolved failure(s); the pack cannot be published.");

        object report = formatVersion == 3 ? new
        {
            formatVersion,
            totalBrands = document.Brands.Count,
            totalAppearances = ordered.Length,
            unresolvedFailures = unresolved,
            appearances = ordered
        } : new
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
