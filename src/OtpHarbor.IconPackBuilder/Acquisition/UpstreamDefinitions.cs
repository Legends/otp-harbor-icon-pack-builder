using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Acquisition;

public sealed record ResolvedUpstream(
    string Provider,
    string Identity,
    string? Version,
    string? Revision,
    string SourceUrl,
    string DownloadUrl);

public interface IUpstreamDefinition
{
    string Id { get; }
    Task<ResolvedUpstream> ResolveLatestAsync(IRemoteContentClient client, CancellationToken cancellationToken);
    Task CreateSourceArchiveAsync(ResolvedUpstream source, string destinationPath, IRemoteContentClient client, CancellationToken cancellationToken);
}

public abstract class GitHubReleaseDefinition(string id, string repository, string? assetName) : IUpstreamDefinition
{
    private const long MaximumApiBytes = 2L * 1024 * 1024;
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;

    public string Id { get; } = id;

    public async Task<ResolvedUpstream> ResolveLatestAsync(IRemoteContentClient client, CancellationToken cancellationToken)
    {
        var apiUrl = $"https://api.github.com/repos/{repository}/releases/latest";
        using var document = ParseJson(await client.GetStringAsync(apiUrl, MaximumApiBytes, cancellationToken), Id, apiUrl);
        var root = document.RootElement;
        var tag = RequiredString(root, "tag_name", apiUrl);
        var sourceUrl = RequiredString(root, "html_url", apiUrl);
        string downloadUrl;
        if (assetName is null)
        {
            downloadUrl = $"https://codeload.github.com/{repository}/zip/refs/tags/{Uri.EscapeDataString(tag)}";
        }
        else
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"{Id}: latest GitHub release does not contain an assets array.");
            downloadUrl = assets.EnumerateArray()
                .Where(x => x.TryGetProperty("name", out var name) && name.GetString() == assetName)
                .Select(x => RequiredString(x, "browser_download_url", apiUrl))
                .SingleOrDefault()
                ?? throw new InvalidDataException($"{Id}: release '{tag}' does not contain required asset '{assetName}'.");
        }

        return new ResolvedUpstream(Id, tag, tag, null, sourceUrl, downloadUrl);
    }

    public Task CreateSourceArchiveAsync(ResolvedUpstream source, string destinationPath, IRemoteContentClient client, CancellationToken cancellationToken)
        => client.DownloadFileAsync(source.DownloadUrl, destinationPath, MaximumArchiveBytes, cancellationToken);

    internal static JsonDocument ParseJson(string json, string provider, string url)
    {
        try { return JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException ex) { throw new InvalidDataException($"{provider}: malformed upstream JSON from '{url}': {ex.Message}", ex); }
    }

    internal static string RequiredString(JsonElement element, string property, string source)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Upstream response from '{source}' is missing '{property}'.");
}

public sealed class AegisUpstreamDefinition()
    : GitHubReleaseDefinition("aegis", "aegis-icons/aegis-icons", "aegis-icons.zip");

public sealed class SimpleIconsUpstreamDefinition()
    : GitHubReleaseDefinition("simple-icons", "simple-icons/simple-icons", null);

public sealed partial class DashboardIconsUpstreamDefinition : IUpstreamDefinition
{
    internal const string AcquisitionMetadataProperty = "_otpHarborAcquisition";
    private const string Repository = "homarr-labs/dashboard-icons";
    private const long MaximumApiBytes = 2L * 1024 * 1024;
    private const long MaximumMetadataBytes = 8L * 1024 * 1024;
    private const long MaximumCompatibleSvgBytes = 1024L * 1024;
    private const long MaximumSvgDownloadBytes = 1024L * 1024;
    private static readonly DateTimeOffset StableTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public string Id => "dashboard-icons";

    public async Task<ResolvedUpstream> ResolveLatestAsync(IRemoteContentClient client, CancellationToken cancellationToken)
    {
        var apiUrl = $"https://api.github.com/repos/{Repository}/commits/main";
        using var document = GitHubReleaseDefinition.ParseJson(
            await client.GetStringAsync(apiUrl, MaximumApiBytes, cancellationToken), Id, apiUrl);
        var sha = GitHubReleaseDefinition.RequiredString(document.RootElement, "sha", apiUrl);
        if (!CommitShaRegex().IsMatch(sha))
            throw new InvalidDataException($"{Id}: upstream returned invalid commit SHA '{sha}'.");
        var sourceUrl = document.RootElement.TryGetProperty("html_url", out var html) && html.ValueKind == JsonValueKind.String
            ? html.GetString()! : $"https://github.com/{Repository}/commit/{sha}";
        return new ResolvedUpstream(Id, sha, null, sha, sourceUrl,
            $"https://raw.githubusercontent.com/{Repository}/{sha}");
    }

    public async Task CreateSourceArchiveAsync(ResolvedUpstream source, string destinationPath, IRemoteContentClient client, CancellationToken cancellationToken)
    {
        var root = source.DownloadUrl.TrimEnd('/');
        var metadataUrl = root + "/metadata.json";
        var metadata = await client.GetBytesAsync(metadataUrl, MaximumMetadataBytes, cancellationToken);
        JsonDocument document;
        string[] slugs;
        try
        {
            document = JsonDocument.Parse(metadata, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("dashboard-icons: metadata root must be an object.");
            slugs = document.RootElement.EnumerateObject()
                .Where(x => x.Value.ValueKind == JsonValueKind.Object
                    && x.Value.TryGetProperty("base", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString()!.Equals("svg", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Name)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"dashboard-icons: malformed metadata from pinned revision '{source.Revision}': {ex.Message}", ex);
        }
        if (slugs.Length is 0 or > 5_000 || slugs.Any(x => !SafeSlugRegex().IsMatch(x)))
            throw new InvalidDataException("dashboard-icons: metadata contains an invalid number of SVG records or an unsafe slug.");

        using (document)
        {
            await using var file = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
            await WriteEntryAsync(archive, "upstream-metadata.json", metadata, cancellationToken);

            var included = new HashSet<string>(StringComparer.Ordinal);
            var skipped = new List<SkippedSvg>();
            var diagnosticsDirectory = Path.Combine(Path.GetDirectoryName(destinationPath)!, "skipped-svgs");
            const int batchSize = 16;
            for (var offset = 0; offset < slugs.Length; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = slugs.Skip(offset).Take(batchSize).ToArray();
                var downloads = batch.Select(slug => DownloadSvgAsync(root, slug, client, cancellationToken)).ToArray();
                var downloaded = await Task.WhenAll(downloads);
                foreach (var item in downloaded)
                {
                    if (item.SkipReason is not null)
                    {
                        var diagnosticPath = item.Data is null ? null : $"skipped-svgs/{item.Slug}.svg";
                        if (item.Data is not null)
                        {
                            Directory.CreateDirectory(diagnosticsDirectory);
                            await File.WriteAllBytesAsync(
                                Path.Combine(diagnosticsDirectory, item.Slug + ".svg"), item.Data, cancellationToken);
                        }
                        skipped.Add(new SkippedSvg(item.Slug, item.Data?.LongLength, diagnosticPath, item.SkipReason));
                        continue;
                    }
                    included.Add(item.Slug);
                    await WriteEntryAsync(archive, $"svg/{item.Slug}.svg", item.Data!, cancellationToken);
                }
            }

            await WriteEntryAsync(archive, "metadata.json",
                CreateProviderMetadata(document.RootElement, included, skipped), cancellationToken);
            var license = await client.GetBytesAsync(root + "/LICENSE", 1024 * 1024, cancellationToken);
            await WriteEntryAsync(archive, "LICENSE", license, cancellationToken);
            await WriteSkippedReportAsync(diagnosticsDirectory, source, skipped, cancellationToken);
        }
    }

    private static async Task<SvgDownload> DownloadSvgAsync(
        string root,
        string slug,
        IRemoteContentClient client,
        CancellationToken cancellationToken)
    {
        var assetPath = $"svg/{slug}.svg";
        byte[]? data = null;
        try
        {
            data = await client.GetBytesAsync(
                $"{root}/svg/{Uri.EscapeDataString(slug)}.svg", MaximumSvgDownloadBytes, cancellationToken);
            ProviderHelpers.ValidateSvg(data, "dashboard-icons", assetPath, MaximumCompatibleSvgBytes);
            return new SvgDownload(slug, data, null);
        }
        catch (DownloadSizeLimitException)
        {
            return new SvgDownload(slug, null, $"SVG exceeds the {MaximumSvgDownloadBytes}-byte download safety limit.");
        }
        catch (DownloadNotFoundException)
        {
            return new SvgDownload(slug, null, "SVG declared by pinned metadata was not found at that revision.");
        }
        catch (InputValidationException ex)
        {
            return new SvgDownload(slug, data, $"SVG failed OTP Harbor validation: {ex.Message}");
        }
    }

    private static byte[] CreateProviderMetadata(
        JsonElement upstreamMetadata,
        IReadOnlySet<string> included,
        IReadOnlyList<SkippedSvg> skipped)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in upstreamMetadata.EnumerateObject()
                         .Where(x => included.Contains(x.Name))
                         .OrderBy(x => x.Name, StringComparer.Ordinal))
                property.WriteTo(writer);
            writer.WritePropertyName(AcquisitionMetadataProperty);
            writer.WriteStartObject();
            writer.WritePropertyName("skippedRecords");
            writer.WriteStartArray();
            foreach (var item in skipped.OrderBy(x => x.SourceId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("sourceId", item.SourceId);
                if (item.SizeBytes is { } sizeBytes) writer.WriteNumber("sizeBytes", sizeBytes);
                else writer.WriteNull("sizeBytes");
                if (item.DiagnosticPath is { } diagnosticPath) writer.WriteString("diagnosticPath", diagnosticPath);
                else writer.WriteNull("diagnosticPath");
                writer.WriteString("reason", item.Reason);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static async Task WriteSkippedReportAsync(
        string directory,
        ResolvedUpstream source,
        IReadOnlyList<SkippedSvg> skipped,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var report = new StringBuilder()
            .AppendLine("# Skipped Dashboard Icons SVGs")
            .AppendLine()
            .AppendLine($"Pinned revision: `{source.Revision}`")
            .AppendLine()
            .AppendLine("These untrusted upstream SVGs were excluded from the generated OTP Harbor pack.")
            .AppendLine()
            .AppendLine("| Icon path | Size (bytes) | Cached diagnostic | Reason |")
            .AppendLine("|---|---:|---|---|");
        foreach (var item in skipped.OrderBy(x => x.SourceId, StringComparer.Ordinal))
        {
            var size = item.SizeBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable";
            var diagnostic = item.DiagnosticPath is null ? "—" : $"`{item.DiagnosticPath}`";
            report.Append("| `svg/").Append(item.SourceId).Append(".svg` | ")
                .Append(size).Append(" | ").Append(diagnostic).Append(" | ")
                .Append(item.Reason.Replace("|", "\\|", StringComparison.Ordinal)).AppendLine(" |");
        }
        if (skipped.Count == 0) report.AppendLine("| _None_ | — | — | — |");
        await File.WriteAllTextAsync(Path.Combine(directory, "report.md"), report.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] content, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = StableTimestamp;
        await using var stream = entry.Open();
        await stream.WriteAsync(content, cancellationToken);
    }

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSlugRegex();

    private sealed record SvgDownload(string Slug, byte[]? Data, string? SkipReason);
    private sealed record SkippedSvg(string SourceId, long? SizeBytes, string? DiagnosticPath, string Reason);
}
