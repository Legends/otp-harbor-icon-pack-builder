using System.Text.Json;
using OtpHarbor.IconPackBuilder.Acquisition;
using OtpHarbor.IconPackBuilder.Packaging;
using OtpHarbor.IconPackBuilder.Providers;
using OtpHarbor.IconPackBuilder.Providers.Aegis;
using OtpHarbor.IconPackBuilder.Providers.DashboardIcons;
using OtpHarbor.IconPackBuilder.Providers.SimpleIcons;
using OtpHarbor.IconPackBuilder.Resolution;

namespace OtpHarbor.IconPackBuilder.Commands;

public static class CliApplication
{
    private const int UsageErrorExitCode = 2;
    private const int BuildErrorExitCode = 3;
    private const int CancellationExitCode = 130;
    private const string DefaultOutputFileName = "otp-harbor-icons.otphicons";
    private const string AnsiGreen = "\u001b[32m";
    private const string AnsiRed = "\u001b[31m";
    private const string AnsiYellow = "\u001b[33m";
    private const string AnsiReset = "\u001b[0m";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default,
        ISourceAcquirer? sourceAcquirer = null,
        TextReader? input = null,
        bool? interactive = null,
        Func<string>? defaultOutputPath = null,
        Func<string>? defaultCachePath = null,
        bool useColor = false)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var providers = CreateProviders();
        var providerLookup = providers.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        input ??= Console.In;
        interactive ??= !Console.IsInputRedirected;
        defaultOutputPath ??= PlatformPaths.GetDefaultOutputPath;
        defaultCachePath ??= PlatformPaths.GetCacheDirectory;

        if (args.Length > 0 && args[0] is "--help" or "-h" or "help")
        {
            PrintHelp(output, providers);
            return 0;
        }
        if (args.Length > 0 && !args[0].Equals("build", StringComparison.OrdinalIgnoreCase))
        {
            WriteStatusLine(error, $"[X] Unknown command '{args[0]}'. Use 'build' or '--help'.", AnsiRed, useColor);
            return UsageErrorExitCode;
        }

        SourceAcquirer? ownedAcquirer = null;
        try
        {
            var options = ParseOptions(args, providerLookup);
            if (options.ShowHelp)
            {
                PrintHelp(output, providers);
                return 0;
            }

            var useInteractivePrompts = interactive.Value && !options.NonInteractive;
            var outputPath = await ResolveOutputPathAsync(options.OutputPath, defaultOutputPath(), useInteractivePrompts,
                input, output, cancellationToken);
            if (outputPath is null) return 1;

            var fullOutput = Path.GetFullPath(outputPath);
            var conflictPath = Path.GetFullPath(options.ConflictPath ?? Path.ChangeExtension(fullOutput, ".conflicts.json"));
            var visualReportPath = Path.ChangeExtension(fullOutput, ".visual-report.json");
            if (fullOutput.Equals(conflictPath, StringComparison.OrdinalIgnoreCase))
                throw new InputValidationException("The output and conflict-report paths must be different.");
            if (fullOutput.Equals(visualReportPath, StringComparison.OrdinalIgnoreCase)
                || conflictPath.Equals(visualReportPath, StringComparison.OrdinalIgnoreCase))
                throw new InputValidationException("The output, conflict-report, and visual-report paths must be different.");

            if (!await ConfirmOverwriteAsync(fullOutput, options.Force, useInteractivePrompts, input, output, cancellationToken))
            {
                if (useInteractivePrompts)
                {
                    WriteStatusLine(output, "[!] Build cancelled; the existing output was not changed.", AnsiYellow, useColor);
                    return 1;
                }
                throw new InputValidationException($"Output file already exists: {fullOutput}. Use --force to replace it.");
            }

            var cachePath = Path.GetFullPath(options.CachePath ?? defaultCachePath());
            if (useInteractivePrompts) PrintHeader(output, fullOutput);
            await output.WriteLineAsync(options.Offline
                ? "Loading local or cached source archives..."
                : "Resolving and acquiring pinned upstream sources...");

            ownedAcquirer = sourceAcquirer is null ? new SourceAcquirer() : null;
            var acquirer = sourceAcquirer ?? ownedAcquirer!;
            var sourceInputs = await acquirer.AcquireAsync(
                options.LocalOverrides,
                new SourceAcquisitionOptions(cachePath, options.Offline, options.Refresh),
                cancellationToken);
            ValidateAcquiredInputs(sourceInputs, providerLookup);
            PrintResolvedSources(output, sourceInputs);

            await output.WriteLineAsync("Building canonical catalog...");
            var catalogs = new List<ProviderCatalog>();
            foreach (var sourceInput in sourceInputs.OrderBy(x => x.Provider, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                catalogs.Add(await providerLookup[sourceInput.Provider].LoadAsync(sourceInput, cancellationToken));
            }

            var mappings = options.MappingsPath is null
                ? await MappingLoader.LoadDefaultAsync(cancellationToken)
                : await MappingLoader.LoadAsync(options.MappingsPath, cancellationToken);
            PackBuildResult result;
            var progress = new CatalogProgressRenderer(output, useInteractivePrompts);
            try
            {
                result = new BrandMerger().Merge(catalogs, mappings, options.ProviderPreference,
                    progress: progress.Report);
                progress.Complete();
            }
            catch (BuildConflictException ex)
            {
                progress.Clear();
                await WriteConflictReportAsync(conflictPath, ex.Conflicts, cancellationToken);
                WriteStatusLine(error, $"[X] Build failed with {ex.Conflicts.Count} conflict(s).", AnsiRed, useColor);
                foreach (var conflict in ex.Conflicts)
                    await error.WriteLineAsync($"Conflict [{conflict.Type}] {conflict.Key}: {conflict.Message} Claims: {string.Join(", ", conflict.Claims)}");
                await error.WriteLineAsync($"Machine-readable conflict report: {conflictPath}");
                return BuildErrorExitCode;
            }
            catch
            {
                progress.Clear();
                throw;
            }

            var outputDirectory = Path.GetDirectoryName(fullOutput)!;
            Directory.CreateDirectory(outputDirectory);
            var temporary = Path.Combine(outputDirectory, $".{Path.GetFileName(fullOutput)}.{Guid.NewGuid():N}.tmp");
            var temporaryVisualReport = Path.Combine(outputDirectory, $".{Path.GetFileName(visualReportPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await PackSerializer.WriteAsync(temporary, result, cancellationToken);
                await output.WriteLineAsync("Validating completed pack...");
                await PackReader.ReadAndValidateAsync(temporary, cancellationToken);
                await VisualVerificationReportWriter.WriteAsync(temporaryVisualReport, result.VisualVerification, cancellationToken);

                // A successful pack must never be accompanied by a stale report from an older failed build.
                if (File.Exists(conflictPath)) File.Delete(conflictPath);
                File.Move(temporary, fullOutput, overwrite: options.Force || useInteractivePrompts);
                File.Move(temporaryVisualReport, visualReportPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (File.Exists(temporaryVisualReport)) File.Delete(temporaryVisualReport);
            }

            PrintSummary(output, result.Summary, result.Document.Sources, fullOutput, visualReportPath, useColor);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WriteStatusLine(error, "[!] Build cancelled.", AnsiYellow, useColor);
            return CancellationExitCode;
        }
        catch (Exception ex) when (ex is InputValidationException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or HttpRequestException
            or JsonException
            or System.Xml.XmlException
            or ArgumentException
            or NotSupportedException)
        {
            WriteStatusLine(error, $"[X] Build failed: {ex.Message}", AnsiRed, useColor);
            return BuildErrorExitCode;
        }
        finally
        {
            ownedAcquirer?.Dispose();
        }
    }

    private static IReadOnlyList<IIconSourceProvider> CreateProviders() =>
    [
        new AegisProvider(),
        new DashboardIconsProvider(),
        new SimpleIconsProvider()
    ];

    private static CliOptions ParseOptions(string[] args, IReadOnlyDictionary<string, IIconSourceProvider> providers)
    {
        string? outputPath = null;
        string? conflictPath = null;
        string? mappingsPath = null;
        string? cachePath = null;
        IReadOnlyList<string>? preference = null;
        var localOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var offline = false;
        var refresh = false;
        var nonInteractive = false;
        var force = false;
        var start = args.Length == 0 ? 0 : 1;

        for (var i = start; i < args.Length; i++)
        {
            var option = args[i];
            if (option is "--help" or "-h")
                return new CliOptions(null, null, null, null, localOverrides, null, false, false, false, false, true);
            switch (option.ToLowerInvariant())
            {
                case "--offline": offline = true; continue;
                case "--refresh": refresh = true; continue;
                case "--non-interactive": nonInteractive = true; continue;
                case "--force": force = true; continue;
            }

            if (!option.StartsWith("--", StringComparison.Ordinal))
                throw new InputValidationException($"Unexpected argument '{option}'.");
            if (++i >= args.Length) throw new InputValidationException($"Option '{option}' requires a value.");
            var value = args[i];
            var optionName = option[2..];
            if (providers.TryGetValue(optionName, out var provider))
            {
                if (!localOverrides.TryAdd(provider.Id, value))
                    throw new InputValidationException($"Provider option '--{provider.Id}' may be specified only once.");
                continue;
            }

            switch (optionName.ToLowerInvariant())
            {
                case "output": outputPath = SingleValue(outputPath, value, option); break;
                case "mappings": mappingsPath = SingleValue(mappingsPath, value, option); break;
                case "cache": cachePath = SingleValue(cachePath, value, option); break;
                case "conflict-report": conflictPath = SingleValue(conflictPath, value, option); break;
                case "provider-preference":
                    if (preference is not null) throw new InputValidationException("--provider-preference may be specified only once.");
                    preference = ValidatePreference(value, providers);
                    break;
                default: throw new InputValidationException($"Unknown option '{option}'.");
            }
        }

        if (offline && refresh) throw new InputValidationException("--offline and --refresh cannot be used together.");
        return new CliOptions(outputPath, conflictPath, mappingsPath, cachePath, localOverrides, preference,
            offline, refresh, nonInteractive, force, false);
    }

    private static string SingleValue(string? current, string value, string option)
        => current is null ? value : throw new InputValidationException($"Option '{option}' may be specified only once.");

    private static IReadOnlyList<string> ValidatePreference(
        string value,
        IReadOnlyDictionary<string, IIconSourceProvider> providers)
    {
        var raw = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Length == 0) throw new InputValidationException("--provider-preference cannot be empty.");
        var normalized = new List<string>(raw.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in raw)
        {
            if (!providers.TryGetValue(id, out var provider))
                throw new InputValidationException($"Unknown provider '{id}' in --provider-preference.");
            if (!seen.Add(provider.Id))
                throw new InputValidationException($"Duplicate provider '{id}' in --provider-preference.");
            normalized.Add(provider.Id);
        }
        return normalized;
    }

    private static async Task<string?> ResolveOutputPathAsync(
        string? requestedPath,
        string proposedPath,
        bool interactive,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath)) return NormalizeOutputPath(requestedPath);
        var normalizedDefault = NormalizeOutputPath(proposedPath);
        if (!interactive) return normalizedDefault;

        await output.WriteLineAsync("OTP Harbor Icon Pack Builder");
        await output.WriteLineAsync("----------------------------");
        await output.WriteLineAsync();
        await output.WriteLineAsync("The latest supported Aegis Icons, Simple Icons, and Dashboard Icons sources will be merged.");
        await output.WriteLineAsync();
        await output.WriteLineAsync("Output:");
        await output.WriteLineAsync($"  {Path.GetFullPath(normalizedDefault)}");
        await output.WriteAsync("Use this location? [Y/n]: ");
        var answer = await ReadLineAsync(input, cancellationToken);
        if (IsYes(answer, defaultAnswer: true)) return normalizedDefault;

        await output.WriteAsync("Enter output directory or full output file path: ");
        var custom = await ReadLineAsync(input, cancellationToken);
        if (string.IsNullOrWhiteSpace(custom))
            throw new InputValidationException("An output directory or file path is required.");
        return NormalizeOutputPath(custom);
    }

    private static string NormalizeOutputPath(string path)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0) throw new InputValidationException("The output path cannot be empty.");
        if (Directory.Exists(path)
            || path.EndsWith(Path.DirectorySeparatorChar)
            || path.EndsWith(Path.AltDirectorySeparatorChar)
            || !Path.HasExtension(path))
            path = Path.Combine(path, DefaultOutputFileName);
        else if (!path.EndsWith(".otphicons", StringComparison.OrdinalIgnoreCase))
            path = Path.ChangeExtension(path, ".otphicons");
        return path;
    }

    private static async Task<bool> ConfirmOverwriteAsync(
        string path,
        bool force,
        bool interactive,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || force) return true;
        if (!interactive) return false;
        await output.WriteLineAsync();
        await output.WriteLineAsync("The output file already exists.");
        await output.WriteAsync("Replace it? [Y/n]: ");
        return IsYes(await ReadLineAsync(input, cancellationToken), defaultAnswer: true);
    }

    private static bool IsYes(string? value, bool defaultAnswer)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultAnswer;
        return value.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadLineAsync(TextReader input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var line = await input.ReadLineAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return line;
    }

    private static void ValidateAcquiredInputs(
        IReadOnlyList<IconSourceInput> inputs,
        IReadOnlyDictionary<string, IIconSourceProvider> providers)
    {
        var duplicates = inputs.GroupBy(x => x.Provider, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicates is not null) throw new InputValidationException($"Acquisition returned provider '{duplicates.Key}' more than once.");
        foreach (var sourceInput in inputs)
            if (!providers.ContainsKey(sourceInput.Provider))
                throw new InputValidationException($"Acquisition returned unknown provider '{sourceInput.Provider}'.");
        var missing = providers.Keys.Where(id => !inputs.Any(x => x.Provider.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InputValidationException($"Acquisition did not provide required source(s): {string.Join(", ", missing)}.");
    }

    private static async Task WriteConflictReportAsync(string path, IReadOnlyList<BuildConflict> conflicts, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var ordered = conflicts.OrderBy(x => x.Type, StringComparer.Ordinal).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray();
            await File.WriteAllBytesAsync(temporary,
                JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, conflicts = ordered }, PackSerializer.JsonOptions), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void PrintHeader(TextWriter output, string outputPath)
    {
        output.WriteLine();
        output.WriteLine($"Output: {outputPath}");
        output.WriteLine();
    }

    private static void PrintResolvedSources(TextWriter output, IReadOnlyList<IconSourceInput> sources)
    {
        foreach (var source in sources.OrderBy(x => x.Provider, StringComparer.Ordinal))
        {
            var identity = source.Version ?? source.Revision ?? "local override";
            output.WriteLine($"Source {source.Provider}: {identity}");
            var sourceDirectory = Path.GetDirectoryName(source.ArchivePath);
            var skippedReport = sourceDirectory is null
                ? null
                : Path.Combine(sourceDirectory, "skipped-svgs", "report.md");
            if (skippedReport is not null && File.Exists(skippedReport))
                output.WriteLine($"Skipped SVG report: {skippedReport}");
        }
    }

    private static void PrintSummary(
        TextWriter output,
        BuildSummary summary,
        IReadOnlyList<PackSource> sources,
        string path,
        string visualReportPath,
        bool useColor)
    {
        output.WriteLine();
        WriteStatusLine(output, "[+] Icon pack created successfully.", AnsiGreen, useColor);
        output.WriteLine($"Output: {path}");
        output.WriteLine($"Visual verification report: {visualReportPath}");
        output.WriteLine($"Sources: {string.Join(", ", sources.OrderBy(x => x.Provider, StringComparer.Ordinal).Select(x => $"{x.Provider}={x.Version ?? x.Revision ?? "local"}"))}");
        output.WriteLine($"Records: {string.Join(", ", summary.RecordsByProvider.Select(x => $"{x.Key}={x.Value}"))}");
        output.WriteLine($"Canonical brands: {summary.CanonicalBrands}; aliases: {summary.Aliases}; merged records: {summary.BrandsMerged}");
        output.WriteLine($"Conflicts: {summary.Conflicts}; suppressed ambiguous aliases: {summary.SuppressedAliases}; invalid/skipped records: {summary.SkippedRecords}");
        output.WriteLine($"Selected icons: {string.Join(", ", summary.SelectedIconsByProvider.Select(x => $"{x.Key}={x.Value}"))}");
        output.WriteLine();
        output.WriteLine("Legal: Third-party icons, names, and trademarks remain subject to their upstream licenses and rights holders.");
        output.WriteLine("License, attribution, and provenance files are included in the generated pack.");
        output.WriteLine("Details: https://github.com/Legends/otp-harbor-icon-pack-builder#legal-and-distribution-notice");
    }

    private static void WriteStatusLine(
        TextWriter writer,
        string message,
        string ansiColor,
        bool useColor)
    {
        if (!useColor)
        {
            writer.WriteLine(message);
            return;
        }

        writer.Write(ansiColor);
        writer.Write(message);
        writer.WriteLine(AnsiReset);
    }

    private static void PrintHelp(TextWriter writer, IReadOnlyList<IIconSourceProvider> providers)
    {
        writer.WriteLine("OTP Harbor Icon Pack Builder");
        writer.WriteLine("Usage: OtpHarbor.IconPackBuilder [build] [options]");
        writer.WriteLine("By default, all supported sources are downloaded, cached, merged, validated, and written to your Downloads directory.");
        writer.WriteLine($"Local source overrides: {string.Join(' ', providers.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => $"--{x.Id} <zip>"))}");
        writer.WriteLine("Options:");
        writer.WriteLine("  --output <path>              Output directory or .otphicons file");
        writer.WriteLine("  --offline                    Use only local overrides and validated cache entries");
        writer.WriteLine("  --refresh                    Redownload the currently resolved upstream versions");
        writer.WriteLine("  --non-interactive            Never prompt");
        writer.WriteLine("  --force                      Replace an existing output");
        writer.WriteLine("  --cache <directory>          Override the platform cache location");
        writer.WriteLine("  --mappings <directory>       Override mapping files");
        writer.WriteLine("  --provider-preference <ids>  Comma-separated provider IDs");
        writer.WriteLine("  --conflict-report <json>     Override conflict-report path");
    }

    private sealed record CliOptions(
        string? OutputPath,
        string? ConflictPath,
        string? MappingsPath,
        string? CachePath,
        IReadOnlyDictionary<string, string> LocalOverrides,
        IReadOnlyList<string>? ProviderPreference,
        bool Offline,
        bool Refresh,
        bool NonInteractive,
        bool Force,
        bool ShowHelp);
}
