namespace OtpHarbor.IconPackBuilder.Providers;

public sealed record ArchiveLimits(
    int MaxEntries = 20_000,
    long MaxCompressedBytes = 512L * 1024 * 1024,
    long MaxUncompressedBytes = 2L * 1024 * 1024 * 1024,
    long MaxEntryBytes = 10L * 1024 * 1024,
    long MaxSvgBytes = 1024L * 1024,
    double MaxCompressionRatio = 100.0);
