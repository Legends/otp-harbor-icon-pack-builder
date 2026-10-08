using System.IO.Compression;
using System.Text;
using System.Text.Json;
using OtpHarbor.IconPackBuilder.Acquisition;
using OtpHarbor.IconPackBuilder.Providers.DashboardIcons;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class AcquisitionTests
{
    [Fact]
    public async Task ResolvedImmutableSourceIsCachedAndReused()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        var definition = new CopyingDefinition("aegis", "v7", fixture.Path);
        using var acquirer = new SourceAcquirer([definition], new StubRemoteClient());
        var options = new SourceAcquisitionOptions(fixture.FilePath("cache"));

        var first = Assert.Single(await acquirer.AcquireAsync(new Dictionary<string, string>(), options, default));
        var second = Assert.Single(await acquirer.AcquireAsync(new Dictionary<string, string>(), options, default));

        Assert.Equal(2, definition.ResolveCount);
        Assert.Equal(1, definition.CreateCount);
        Assert.Equal(first.ArchivePath, second.ArchivePath);
        Assert.Equal("v7", second.Version);
        Assert.Equal("https://example.invalid/aegis/v7", second.SourceUrl);
    }

    [Fact]
    public async Task RefreshForcesRedownloadOfResolvedSource()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        var definition = new CopyingDefinition("aegis", "v7", fixture.Path);
        using var acquirer = new SourceAcquirer([definition], new StubRemoteClient());
        var cache = fixture.FilePath("cache");

        await acquirer.AcquireAsync(new Dictionary<string, string>(), new SourceAcquisitionOptions(cache), default);
        await acquirer.AcquireAsync(new Dictionary<string, string>(), new SourceAcquisitionOptions(cache, Refresh: true), default);

        Assert.Equal(2, definition.CreateCount);
    }

    [Fact]
    public async Task OfflineUsesValidatedCacheWithoutNetworkResolution()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        var definition = new CopyingDefinition("aegis", "v7", fixture.Path);
        using var acquirer = new SourceAcquirer([definition], new StubRemoteClient());
        var cache = fixture.FilePath("cache");
        await acquirer.AcquireAsync(new Dictionary<string, string>(), new SourceAcquisitionOptions(cache), default);

        var cached = Assert.Single(await acquirer.AcquireAsync(new Dictionary<string, string>(),
            new SourceAcquisitionOptions(cache, Offline: true), default));

        Assert.Equal(1, definition.ResolveCount);
        Assert.Equal(1, definition.CreateCount);
        Assert.Equal("v7", cached.Version);
    }

    [Fact]
    public async Task OfflineDoesNotSilentlyUseCacheFromAnotherSupportedIdentity()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        var cache = fixture.FilePath("cache");
        using (var first = new SourceAcquirer([new CopyingDefinition("aegis", "v7", fixture.Path)], new StubRemoteClient()))
            await first.AcquireAsync(new Dictionary<string, string>(), new SourceAcquisitionOptions(cache), default);
        using var upgraded = new SourceAcquirer([new CopyingDefinition("aegis", "v8", fixture.Path)], new StubRemoteClient());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => upgraded.AcquireAsync(
            new Dictionary<string, string>(), new SourceAcquisitionOptions(cache, Offline: true), default));

        Assert.Contains("offline mode", exception.Message);
    }

    [Fact]
    public async Task ExplicitLocalOverrideTakesPrecedenceOverNetworkAndCache()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        var definition = new CopyingDefinition("aegis", "v7", fixture.Path);
        using var acquirer = new SourceAcquirer([definition], new StubRemoteClient());

        var result = Assert.Single(await acquirer.AcquireAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AEGIS"] = fixture.Path },
            new SourceAcquisitionOptions(fixture.FilePath("cache"), Offline: true), default));

        Assert.Equal(Path.GetFullPath(fixture.Path), result.ArchivePath);
        Assert.Equal(0, definition.ResolveCount);
        Assert.Equal(0, definition.CreateCount);
    }

    [Fact]
    public async Task CorruptedCacheIsRejectedAndReplacedOnline()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        var definition = new CopyingDefinition("aegis", "v7", fixture.Path);
        using var acquirer = new SourceAcquirer([definition], new StubRemoteClient());
        var options = new SourceAcquisitionOptions(fixture.FilePath("cache"));
        var first = Assert.Single(await acquirer.AcquireAsync(new Dictionary<string, string>(), options, default));
        await File.WriteAllTextAsync(first.ArchivePath, "corrupt");

        var repaired = Assert.Single(await acquirer.AcquireAsync(new Dictionary<string, string>(), options, default));

        Assert.Equal(2, definition.CreateCount);
        Assert.True(new FileInfo(repaired.ArchivePath).Length > "corrupt".Length);
    }

    [Fact]
    public async Task CorruptedCacheFailsClearlyOffline()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        var definition = new CopyingDefinition("aegis", "v7", fixture.Path);
        using var acquirer = new SourceAcquirer([definition], new StubRemoteClient());
        var cache = fixture.FilePath("cache");
        var first = Assert.Single(await acquirer.AcquireAsync(new Dictionary<string, string>(),
            new SourceAcquisitionOptions(cache), default));
        await File.WriteAllTextAsync(first.ArchivePath, "corrupt");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => acquirer.AcquireAsync(
            new Dictionary<string, string>(), new SourceAcquisitionOptions(cache, Offline: true), default));

        Assert.Contains("offline mode", exception.Message);
        Assert.Contains("aegis", exception.Message);
    }

    [Fact]
    public async Task NetworkFailureIncludesProviderAndPinnedIdentity()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        var definition = new CopyingDefinition("aegis", "v7", fixture.Path) { CreateFailure = new HttpRequestException("connection failed") };
        using var acquirer = new SourceAcquirer([definition], new StubRemoteClient());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => acquirer.AcquireAsync(
            new Dictionary<string, string>(), new SourceAcquisitionOptions(fixture.FilePath("cache")), default));

        Assert.Contains("aegis", exception.Message);
        Assert.Contains("v7", exception.Message);
        Assert.Contains("https://example.invalid/download/v7", exception.Message);
    }

    [Fact]
    public async Task ExternalCancellationIsNotConvertedToDataFailure()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "fixture" });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var acquirer = new SourceAcquirer([new CancellingDefinition()], new StubRemoteClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquirer.AcquireAsync(
            new Dictionary<string, string>(), new SourceAcquisitionOptions(fixture.FilePath("cache")), cancellation.Token));
    }

    [Fact]
    public async Task GitHubReleaseDefinitionsUseTestedSupportedVersions()
    {
        var client = new StubRemoteClient();

        var aegis = await new AegisUpstreamDefinition().ResolveSupportedAsync(client, default);
        var simple = await new SimpleIconsUpstreamDefinition().ResolveSupportedAsync(client, default);

        Assert.Equal("2026-07-01", aegis.Version);
        Assert.Equal("ec6dae28f1fa87688d691f2c43265083a1797b3c", aegis.Revision);
        Assert.Equal("b48028973d8c8b22c941f0bbb5f95d769b9913a40952c0f60a1bbec892346013", aegis.ExpectedDownloadSha256);
        Assert.EndsWith(aegis.Revision!, aegis.SourceUrl, StringComparison.Ordinal);
        Assert.Equal("https://github.com/aegis-icons/aegis-icons/releases/download/2026-07-01/aegis-icons.zip", aegis.DownloadUrl);
        Assert.Equal("16.34.0", simple.Version);
        Assert.Equal("dde88ab37611285a2bf1a7883c62be7e479794ba", simple.Revision);
        Assert.Equal("795fbfcefcd1ea36b1ca67a81bb25a1e47c2f489653e4e5bb5e111f5420dbc9c", simple.ExpectedDownloadSha256);
        Assert.Equal("https://codeload.github.com/simple-icons/simple-icons/zip/refs/tags/16.34.0", simple.DownloadUrl);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task PinnedReleaseHashMismatchIsRejected()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["content.txt"] = "different" });
        var definition = new SimpleIconsUpstreamDefinition();
        var source = await definition.ResolveSupportedAsync(new StubRemoteClient(), default);
        var client = new StubRemoteClient(new Dictionary<string, byte[]>
        {
            [source.DownloadUrl] = await File.ReadAllBytesAsync(fixture.Path)
        });

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => definition.CreateSourceArchiveAsync(
            source, fixture.FilePath("download.zip"), client, default));

        Assert.Contains("SHA-256 mismatch", exception.Message);
    }

    [Fact]
    public async Task DashboardAcquisitionPinsMetadataAndEveryAssetToOneCommit()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var sha = DashboardIconsUpstreamDefinition.SupportedRevision;
        var root = $"https://raw.githubusercontent.com/homarr-labs/dashboard-icons/{sha}";
        var client = new StubRemoteClient(new Dictionary<string, byte[]>
        {
            [root + "/metadata.json"] = Bytes("{\"alpha\":{\"base\":\"svg\"},\"png-only\":{\"base\":\"png\"}}"),
            [root + "/svg/alpha.svg"] = Bytes(TestData.Svg),
            [root + "/LICENSE"] = Bytes("fixture license"),
            [root + "/README.md"] = Bytes("fixture readme")
        });
        var definition = new DashboardIconsUpstreamDefinition();
        var resolved = await definition.ResolveSupportedAsync(client, default);
        var destination = fixture.FilePath("dashboard.zip");

        await definition.CreateSourceArchiveAsync(resolved, destination, client, default);

        Assert.Equal(sha, resolved.Revision);
        Assert.All(client.Calls, url => Assert.StartsWith(root, url, StringComparison.Ordinal));
        using var file = File.OpenRead(destination);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.Equal(["upstream-metadata.json", "svg/alpha.svg", "metadata.json", "LICENSE", "README.md"], zip.Entries.Select(x => x.FullName));
        Assert.DoesNotContain(client.Calls, x => x.Contains("png-only", StringComparison.Ordinal));
        Assert.All(zip.Entries, entry => Assert.Equal(1980, entry.LastWriteTime.Year));
    }

    [Fact]
    public async Task DashboardAcquisitionSkipsAndReportsOversizedOrMissingSvgWithoutMixingRevisions()
    {
        using var fixture = new TestArchive(new Dictionary<string, string> { ["placeholder"] = "x" });
        var sha = new string('c', 40);
        var root = $"https://raw.githubusercontent.com/homarr-labs/dashboard-icons/{sha}";
        var missingUrl = root + "/svg/missing.svg";
        var client = new StubRemoteClient(new Dictionary<string, byte[]>
        {
            [root + "/metadata.json"] = Bytes("{\"alpha\":{\"base\":\"svg\"},\"anchor\":{\"base\":\"svg\"},\"missing\":{\"base\":\"svg\"},\"unsafe\":{\"base\":\"svg\"}}"),
            [root + "/svg/alpha.svg"] = Bytes(TestData.Svg),
            [root + "/svg/anchor.svg"] = new byte[1024 * 1024 + 1],
            [root + "/svg/unsafe.svg"] = Bytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/><path d=\"M0 0h1v1z\"/></svg>"),
            [root + "/LICENSE"] = Bytes("fixture license"),
            [root + "/README.md"] = Bytes("fixture readme")
        }, new HashSet<string>(StringComparer.Ordinal) { missingUrl });
        var source = new ResolvedUpstream("dashboard-icons", sha, null, sha,
            $"https://github.com/homarr-labs/dashboard-icons/commit/{sha}", root);
        var destination = fixture.FilePath("dashboard-oversized.zip");

        await new DashboardIconsUpstreamDefinition().CreateSourceArchiveAsync(source, destination, client, default);
        var catalog = await new DashboardIconsProvider().LoadAsync(
            new IconSourceInput("dashboard-icons", destination, Revision: sha, SourceUrl: source.SourceUrl));

        Assert.Equal("alpha", Assert.Single(catalog.Records).SourceId);
        Assert.Equal(3, catalog.SkippedRecords);
        Assert.Equal("3", catalog.Metadata["acquisitionSkippedRecords"]);
        using var file = File.OpenRead(destination);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.Null(zip.GetEntry("svg/anchor.svg"));
        using var metadata = JsonDocument.Parse(zip.GetEntry("metadata.json")!.Open());
        Assert.False(metadata.RootElement.TryGetProperty("anchor", out _));
        Assert.False(metadata.RootElement.TryGetProperty("missing", out _));
        Assert.False(metadata.RootElement.TryGetProperty("unsafe", out _));
        var skipped = metadata.RootElement.GetProperty(DashboardIconsUpstreamDefinition.AcquisitionMetadataProperty)
            .GetProperty("skippedRecords");
        Assert.Equal(["anchor", "missing", "unsafe"], skipped.EnumerateArray().Select(x => x.GetProperty("sourceId").GetString()));
        Assert.All(client.Calls, url => Assert.StartsWith(root, url, StringComparison.Ordinal));
        var diagnostics = Path.Combine(fixture.DirectoryPath, "skipped-svgs");
        Assert.False(File.Exists(Path.Combine(diagnostics, "anchor.svg")));
        Assert.True(File.Exists(Path.Combine(diagnostics, "unsafe.svg")));
        Assert.False(File.Exists(Path.Combine(diagnostics, "missing.svg")));
        var report = await File.ReadAllTextAsync(Path.Combine(diagnostics, "report.md"));
        Assert.Contains("| Icon path | Size (bytes) |", report);
        Assert.Contains("| `svg/anchor.svg` | unavailable |", report);
        Assert.Contains("| `svg/missing.svg` | unavailable |", report);
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private sealed class CopyingDefinition(string id, string identity, string sourceArchive) : IUpstreamDefinition
    {
        public string Id { get; } = id;
        public string? SupportedIdentity => identity;
        public int ResolveCount { get; private set; }
        public int CreateCount { get; private set; }
        public Exception? CreateFailure { get; init; }

        public Task<ResolvedUpstream> ResolveSupportedAsync(IRemoteContentClient client, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResolveCount++;
            return Task.FromResult(new ResolvedUpstream(Id, identity, identity, null,
                $"https://example.invalid/{Id}/{identity}", $"https://example.invalid/download/{identity}"));
        }

        public Task CreateSourceArchiveAsync(
            ResolvedUpstream source,
            string destinationPath,
            IRemoteContentClient client,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            if (CreateFailure is not null) throw CreateFailure;
            File.Copy(sourceArchive, destinationPath);
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingDefinition : IUpstreamDefinition
    {
        public string Id => "aegis";

        public Task<ResolvedUpstream> ResolveSupportedAsync(IRemoteContentClient client, CancellationToken cancellationToken)
            => Task.FromCanceled<ResolvedUpstream>(cancellationToken);

        public Task CreateSourceArchiveAsync(ResolvedUpstream source, string destinationPath,
            IRemoteContentClient client, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubRemoteClient(
        IReadOnlyDictionary<string, byte[]>? responses = null,
        IReadOnlySet<string>? notFoundUrls = null) : IRemoteContentClient
    {
        private readonly IReadOnlyDictionary<string, byte[]> _responses = responses ?? new Dictionary<string, byte[]>();
        private readonly IReadOnlySet<string> _notFoundUrls = notFoundUrls ?? new HashSet<string>();
        public List<string> Calls { get; } = [];

        public async Task<string> GetStringAsync(string url, long maximumBytes, CancellationToken cancellationToken)
            => Encoding.UTF8.GetString(await GetBytesAsync(url, maximumBytes, cancellationToken));

        public Task<byte[]> GetBytesAsync(string url, long maximumBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(url);
            if (_notFoundUrls.Contains(url)) throw new DownloadNotFoundException($"Fixture URL not found: {url}");
            if (!_responses.TryGetValue(url, out var result)) throw new HttpRequestException($"Unexpected URL: {url}");
            if (result.LongLength > maximumBytes) throw new DownloadSizeLimitException("Fixture exceeds limit.");
            return Task.FromResult(result);
        }

        public async Task DownloadFileAsync(string url, string destinationPath, long maximumBytes, CancellationToken cancellationToken)
            => await File.WriteAllBytesAsync(destinationPath, await GetBytesAsync(url, maximumBytes, cancellationToken), cancellationToken);
    }
}
