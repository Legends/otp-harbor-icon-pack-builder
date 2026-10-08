using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void ZipSlipPathIsRejected()
    {
        using var archive = new TestArchive(new Dictionary<string, string> { ["../escape.svg"] = TestData.Svg });
        var exception = Assert.Throws<InputValidationException>(() => SecureZipArchive.Open(archive.Path));
        Assert.Contains("unsafe parent-directory", exception.Message);
    }

    [Fact]
    public void AbsolutePathIsRejected()
    {
        using var archive = new TestArchive(new Dictionary<string, string> { ["C:/escape.svg"] = TestData.Svg });
        Assert.Throws<InputValidationException>(() => SecureZipArchive.Open(archive.Path));
    }

    [Fact]
    public void CaseInsensitiveDuplicateArchivePathsAreRejected()
    {
        using var archive = new TestArchive(new Dictionary<string, string>
        {
            ["icons/GitHub.svg"] = TestData.Svg,
            ["icons/github.svg"] = TestData.Svg
        });
        var exception = Assert.Throws<InputValidationException>(() => SecureZipArchive.Open(archive.Path));
        Assert.Contains("duplicate conflicting path", exception.Message);
    }

    [Fact]
    public void ArchiveEntryCountLimitIsEnforced()
    {
        using var archive = new TestArchive(new Dictionary<string, string> { ["one"] = "1", ["two"] = "2" });
        var exception = Assert.Throws<InputValidationException>(() => SecureZipArchive.Open(archive.Path, new ArchiveLimits(MaxEntries: 1)));
        Assert.Contains("entries", exception.Message);
    }

    [Fact]
    public void ArchiveUncompressedSizeLimitIsEnforced()
    {
        using var archive = new TestArchive(new Dictionary<string, string> { ["large"] = new string('x', 100) });
        var exception = Assert.Throws<InputValidationException>(() => SecureZipArchive.Open(archive.Path,
            new ArchiveLimits(MaxUncompressedBytes: 50, MaxEntryBytes: 200)));
        Assert.Contains("uncompressed size limit", exception.Message);
    }

    [Fact]
    public void DecompressionRatioLimitIsEnforced()
    {
        using var archive = new TestArchive(new Dictionary<string, byte[]> { ["large"] = new byte[1_100_000] });
        var exception = Assert.Throws<InputValidationException>(() => SecureZipArchive.Open(archive.Path,
            new ArchiveLimits(MaxEntryBytes: 2_000_000, MaxUncompressedBytes: 2_000_000, MaxCompressionRatio: 10)));
        Assert.Contains("compression-ratio", exception.Message);
    }
}
