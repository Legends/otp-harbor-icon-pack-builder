using OtpHarbor.IconPackBuilder.Normalization;
using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Resolution;

public sealed record CatalogBuildProgress(int CompletedBrands, int TotalBrands, string CurrentBrandId);

public sealed class BrandMerger
{
    public static readonly IReadOnlyList<string> DefaultProviderPreference = ["aegis", "dashboard-icons", "simple-icons"];

    public PackBuildResult Merge(
        IReadOnlyList<ProviderCatalog> catalogs,
        MappingSet mappings,
        IReadOnlyList<string>? providerPreference = null,
        string packId = "otp-harbor-icons",
        string packName = "OTP Harbor Icons",
        Action<CatalogBuildProgress>? progress = null,
        RightsPolicy rightsPolicy = RightsPolicy.Preserve)
    {
        if (catalogs.Count == 0) throw new InputValidationException("At least one provider input is required.");
        providerPreference ??= DefaultProviderPreference;
        var sourceMappings = mappings.CanonicalBrands.SelectMany(brand => brand.Matches.Select(match => (brand, match)))
            .ToDictionary(x => Key(x.match.Provider, x.match.SourceId), x => x.brand, StringComparer.OrdinalIgnoreCase);
        var explicitlySelected = mappings.SourceOverrides.Values.Select(x => Key(x.Provider, x.SourceId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allProviderRecords = catalogs.SelectMany(x => x.Records)
            .OrderBy(x => x.Provider, StringComparer.Ordinal)
            .ThenBy(x => x.SourceId, StringComparer.Ordinal)
            .ToArray();
        var conflicts = FindDuplicateSources(allProviderRecords);
        var canonicalCandidates = allProviderRecords.Where(x => x.AutomaticCandidate
                || sourceMappings.ContainsKey(Key(x.Provider, x.SourceId))
                || explicitlySelected.Contains(Key(x.Provider, x.SourceId)))
            .ToArray();
        var rightsExcludedRecords = 0;
        var allRecords = canonicalCandidates;
        IReadOnlyDictionary<string, SourceOverride> effectiveSourceOverrides = mappings.SourceOverrides;
        if (rightsPolicy == RightsPolicy.DocumentedOnly)
        {
            allRecords = canonicalCandidates.Where(record =>
                RightsAssessor.IsDocumented(RightsAssessor.Assess(record, mappings.EffectiveRightsOverrides))).ToArray();
            rightsExcludedRecords = canonicalCandidates.Length - allRecords.Length;
            var eligibleKeys = allRecords.Select(x => Key(x.Provider, x.SourceId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            effectiveSourceOverrides = mappings.SourceOverrides
                .Where(x => eligibleKeys.Contains(Key(x.Value.Provider, x.Value.SourceId)))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (allRecords.Length == 0)
                throw new InputValidationException("Rights policy 'documented-only' excluded every source record because no asset-level evidence was available.");
        }
        var nameMappings = BuildNameMappings(mappings.CanonicalBrands);
        var groups = new SortedDictionary<string, BrandGroup>(StringComparer.Ordinal);

        foreach (var record in allRecords)
        {
            CanonicalBrandMapping? manual = null;
            if (!sourceMappings.TryGetValue(Key(record.Provider, record.SourceId), out manual))
            {
                var claims = new HashSet<CanonicalBrandMapping>();
                foreach (var candidate in new[] { record.DisplayName, record.SourceId })
                    if (nameMappings.TryGetValue(IssuerNormalizer.Normalize(candidate), out var matches))
                        foreach (var match in matches) claims.Add(match);
                if (claims.Count == 1) manual = claims.Single();
                else if (claims.Count > 1)
                    conflicts.Add(new BuildConflict("canonicalization", record.Provider + "/" + record.SourceId,
                        "Record matches multiple canonical mapping names.", claims.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray()));
            }

            var id = manual?.Id ?? CanonicalId.FromName(record.DisplayName);
            if (!groups.TryGetValue(id, out var group))
            {
                var known = manual ?? mappings.CanonicalBrands.FirstOrDefault(x => x.Id == id);
                group = new BrandGroup(id, known?.DisplayName ?? record.DisplayName, known);
                groups.Add(id, group);
            }
            else if (manual is null && group.Manual is null
                && IssuerNormalizer.Normalize(group.DisplayName) != IssuerNormalizer.Normalize(record.DisplayName))
            {
                conflicts.Add(new BuildConflict("canonicalization", id,
                    "Different names generated the same canonical ID; add an explicit canonical mapping.",
                    new[] { group.DisplayName, record.DisplayName }.OrderBy(x => x, StringComparer.Ordinal).ToArray()));
            }
            group.Records.Add(record);
        }

        if (conflicts.Count > 0) throw new BuildConflictException(conflicts);

        var pendingBrands = new List<PendingBrand>();
        var icons = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var aliasCandidates = new Dictionary<string, List<AliasCandidate>>(StringComparer.Ordinal);
        var selectedCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var rightsCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var rightsAssessments = new List<RightsAssessmentEntry>();
        var visualVerification = new List<VisualVerificationEntry>();
        var completedBrands = 0;

        foreach (var group in groups.Values)
        {
            progress?.Invoke(new CatalogBuildProgress(completedBrands, groups.Count, group.Id));
            var selected = SelectSource(group, effectiveSourceOverrides, providerPreference);
            var selectedRights = RightsAssessor.Assess(selected.Record, mappings.EffectiveRightsOverrides);
            if (rightsPolicy == RightsPolicy.RequireDocumented && !RightsAssessor.IsDocumented(selectedRights))
                throw new InputValidationException($"Brand '{group.Id}' selected '{selected.Record.Provider}/{selected.Record.SourceId}' with rights status '{RightsAssessor.ToMetadataValue(selectedRights.Status)}'. Add reviewed evidence in rights-assessments.json, choose another source, or use --rights-policy preserve.");
            var iconPath = $"icons/{group.Id}.svg";
            icons.Add(iconPath, selected.Normalized.Svg);
            selectedCounts[selected.Record.Provider] = selectedCounts.GetValueOrDefault(selected.Record.Provider) + 1;
            var rightsStatus = RightsAssessor.ToMetadataValue(selectedRights.Status);
            rightsCounts[rightsStatus] = rightsCounts.GetValueOrDefault(rightsStatus) + 1;
            rightsAssessments.Add(new RightsAssessmentEntry(group.Id, selected.Record.Provider,
                selected.Record.SourceId, selectedRights.Status, selectedRights.Basis,
                selectedRights.LicenseType, selectedRights.EvidenceUrl, selectedRights.Note));

            foreach (var candidate in BuildAliasCandidates(group, mappings.IssuerAliases.GetValueOrDefault(group.Id)))
            {
                var normalized = IssuerNormalizer.Normalize(candidate.Value);
                if (normalized.Length == 0) continue;
                if (!aliasCandidates.TryGetValue(normalized, out var candidates)) aliasCandidates[normalized] = candidates = [];
                candidates.Add(candidate with { BrandId = group.Id });
            }
            var sourceReferences = group.Records
                .OrderBy(x => x.Provider, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal)
                .Select(x => new SourceReference(x.Provider, x.SourceId,
                    SortedMetadata(WithRightsMetadata(x.Metadata,
                        RightsAssessor.Assess(x, mappings.EffectiveRightsOverrides)))))
                .ToArray();
            var selectedMetadata = selected.Record.Metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            AddRightsMetadata(selectedMetadata, selectedRights);
            selectedMetadata["visualNormalization"] = selected.Normalized.Operations.Count == 0
                ? "canonical-pass-through"
                : string.Join(',', selected.Normalized.Operations);
            selectedMetadata["normalizedForegroundColorCount"] = selected.Normalized.ForegroundColors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            pendingBrands.Add(new PendingBrand(group.Id, group.DisplayName, selected.Normalized.BackgroundColor, iconPath,
                new SelectedSource(selected.Record.Provider, selected.Record.SourceId, SortedMetadata(selectedMetadata)), sourceReferences));
            var metrics = selected.Normalized.VisualMetrics;
            visualVerification.Add(new VisualVerificationEntry(
                group.Id,
                selected.Record.Provider,
                selected.Record.SourceId,
                selected.Record.AssetPath,
                selected.Normalized.BackgroundColor,
                selected.Normalized.Operations,
                metrics.MeanAbsoluteChannelDifference,
                metrics.MateriallyDifferentPixels,
                metrics.TotalPixels,
                metrics.SourcePalette,
                metrics.NormalizedPalette,
                metrics.Resolved));
            completedBrands++;
            progress?.Invoke(new CatalogBuildProgress(completedBrands, groups.Count, group.Id));
        }

        var aliasResolution = ResolveAliases(aliasCandidates, pendingBrands.Select(x => x.Id));
        if (aliasResolution.Conflicts.Count > 0) throw new BuildConflictException(aliasResolution.Conflicts);
        var outputBrands = pendingBrands.Select(brand => new PackBrand(
                brand.Id,
                brand.DisplayName,
                brand.BackgroundColor,
                brand.Icon,
                aliasResolution.AliasesByBrand[brand.Id],
                brand.SelectedSource,
                brand.Sources))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
        var issuerAliases = aliasResolution.Index;
        var licenses = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var packSources = new List<PackSource>();
        foreach (var catalog in catalogs.OrderBy(x => x.Provider, StringComparer.Ordinal))
        {
            var licensePaths = new List<string>();
            foreach (var license in catalog.Licenses.OrderBy(x => x.FileName, StringComparer.Ordinal))
            {
                var safeName = CanonicalId.FromName(Path.GetFileNameWithoutExtension(license.FileName));
                var extension = Path.GetExtension(license.FileName).ToLowerInvariant();
                var path = $"licenses/{catalog.Provider}/{safeName}{extension}";
                var suffix = 2;
                while (licenses.ContainsKey(path)) path = $"licenses/{catalog.Provider}/{safeName}-{suffix++}{extension}";
                licenses.Add(path, license.Content);
                licensePaths.Add(path);
            }
            packSources.Add(new PackSource(catalog.Provider, catalog.InputFileName, catalog.InputSha256,
                catalog.Version, catalog.Revision, catalog.SourceUrl,
                SortedMetadata(catalog.Metadata), licensePaths));
        }

        var document = new PackDocument
        {
            PackId = packId,
            Name = packName,
            Sources = packSources,
            Brands = outputBrands,
            IssuerAliases = issuerAliases
        };
        var summary = new BuildSummary(
            catalogs.OrderBy(x => x.Provider, StringComparer.Ordinal).ToDictionary(x => x.Provider, x => x.Records.Count, StringComparer.Ordinal),
            outputBrands.Length, issuerAliases.Count, allRecords.Length - outputBrands.Length, 0,
            aliasResolution.SuppressedAliases,
            catalogs.Sum(x => x.SkippedRecords) + allProviderRecords.Length - allRecords.Length, selectedCounts);
        summary = summary with
        {
            SelectedRightsByStatus = rightsCounts,
            RightsExcludedRecords = rightsExcludedRecords
        };
        return new PackBuildResult(document, icons, licenses, summary,
            visualVerification.OrderBy(x => x.BrandId, StringComparer.Ordinal).ToArray(),
            rightsAssessments.OrderBy(x => x.BrandId, StringComparer.Ordinal).ToArray());
    }

    private static List<BuildConflict> FindDuplicateSources(IReadOnlyList<SourceRecord> records)
        => records.GroupBy(x => Key(x.Provider, x.SourceId), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1)
            .Select(x => new BuildConflict("duplicate-source", x.Key,
                "Provider contains duplicate source records.", x.Select(y => y.AssetPath).OrderBy(y => y, StringComparer.Ordinal).ToArray()))
            .ToList();

    private static Dictionary<string, List<CanonicalBrandMapping>> BuildNameMappings(IReadOnlyList<CanonicalBrandMapping> mappings)
    {
        var result = new Dictionary<string, List<CanonicalBrandMapping>>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
            foreach (var name in mapping.Aliases.Append(mapping.DisplayName).Append(mapping.Id))
            {
                var key = IssuerNormalizer.Normalize(name);
                if (!result.TryGetValue(key, out var list)) result[key] = list = [];
                if (!list.Contains(mapping)) list.Add(mapping);
            }
        return result;
    }

    private static NormalizedSelection SelectSource(BrandGroup group, IReadOnlyDictionary<string, SourceOverride> overrides, IReadOnlyList<string> preference)
    {
        if (overrides.TryGetValue(group.Id, out var sourceOverride))
        {
            var overridden = group.Records.SingleOrDefault(x => x.Provider.Equals(sourceOverride.Provider, StringComparison.OrdinalIgnoreCase)
                && x.SourceId.Equals(sourceOverride.SourceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InputValidationException($"Source override for '{group.Id}' refers to missing record '{sourceOverride.Provider}/{sourceOverride.SourceId}'.");
            return Normalize(overridden, ResolveBackgroundColor(group, overridden), group.Id);
        }

        var failures = new List<string>();
        var normalized = new List<NormalizedSelection>();
        foreach (var record in group.Records)
        {
            try { normalized.Add(Normalize(record, ResolveBackgroundColor(group, record), group.Id)); }
            catch (InputValidationException ex) { failures.Add($"{record.Provider}/{record.SourceId}: {ex.Message}"); }
        }
        if (normalized.Count == 0)
            throw new InputValidationException($"Brand '{group.Id}' has no source that can be normalized: {string.Join("; ", failures)}");
        return normalized.OrderBy(VisualQualityRank)
            .ThenBy(x => PreferenceRank(x.Record.Provider, preference))
            .ThenBy(x => QualityRank(x.Record.Quality)).ThenBy(x => x.Record.Provider, StringComparer.Ordinal)
            .ThenBy(x => x.Record.SourceId, StringComparer.Ordinal).First();
    }

    private static NormalizedSelection Normalize(SourceRecord record, string backgroundColor, string brandId)
    {
        try
        {
            var materializeImplicitBlackFill = record.Provider.Equals("simple-icons", StringComparison.OrdinalIgnoreCase)
                && backgroundColor.Equals("#FFFFFF", StringComparison.Ordinal);
            return new NormalizedSelection(record,
                SvgNormalizer.Normalize(record.Svg, backgroundColor, materializeImplicitBlackFill));
        }
        catch (InputValidationException ex)
        {
            throw new InputValidationException($"{record.Provider}/{record.SourceId} for '{brandId}' could not be normalized: {ex.Message}");
        }
    }

    private static int VisualQualityRank(NormalizedSelection selection)
    {
        var operations = selection.Normalized.Operations;
        var usesRasterFallback = operations.Any(operation =>
            operation.StartsWith("raster-flattened-at-", StringComparison.Ordinal));
        var usesGeneratedLightNeutral = operations.Contains("selected-light-contrast-background", StringComparer.Ordinal);
        var hasSourceBackedBackground = operations.Contains("promoted-full-logo-background", StringComparer.Ordinal)
            || (!usesGeneratedLightNeutral
                && !selection.Normalized.BackgroundColor.Equals("#334155", StringComparison.Ordinal));
        var colorCount = selection.Normalized.ForegroundColors.Count;
        var chromaticColorCount = selection.Normalized.ForegroundColors.Count(IsChromatic);

        // Prefer a faithfully normalized full-color logo over a monochrome badge
        // from an otherwise preferred provider. Provider preference remains the
        // deterministic tie-breaker for candidates with equivalent visual value.
        var vectorQualityRank = chromaticColorCount >= 2 ? 0
            : hasSourceBackedBackground && colorCount >= 1 ? 1
            : chromaticColorCount >= 1 || colorCount >= 2 ? 2
            : hasSourceBackedBackground ? 3
            : colorCount >= 1 ? 4
            : 5;

        // A canonical vector stays sharp at every application scale. Use a
        // verified raster-path fallback only when no faithful vector candidate
        // is available for the canonical brand.
        return vectorQualityRank + (usesRasterFallback ? 10 : 0);
    }

    private static bool IsChromatic(string color)
    {
        if (color.Length != 7 || color[0] != '#') return false;
        var red = Convert.ToByte(color.Substring(1, 2), 16);
        var green = Convert.ToByte(color.Substring(3, 2), 16);
        var blue = Convert.ToByte(color.Substring(5, 2), 16);
        return Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) >= 32;
    }

    private static string ResolveBackgroundColor(BrandGroup group, SourceRecord selected)
    {
        foreach (var candidate in new[]
        {
            group.Manual?.BackgroundColor,
            selected.Metadata.GetValueOrDefault("nativeBrandColor"),
            selected.Metadata.GetValueOrDefault("extractedBrandColor")
        })
        {
            if (candidate is { Length: 7 } && candidate[0] == '#'
                && ProviderHelpers.IsHexColor(candidate[1..]))
                return candidate.ToUpperInvariant();
        }
        return "#334155";
    }

    private static int PreferenceRank(string provider, IReadOnlyList<string> preference)
    {
        for (var index = 0; index < preference.Count; index++)
            if (provider.Equals(preference[index], StringComparison.OrdinalIgnoreCase)) return index;
        return int.MaxValue;
    }

    private static int QualityRank(string quality) => quality switch { "primary" => 0, "default" => 1, _ => 2 };

    private static IReadOnlyList<AliasCandidate> BuildAliasCandidates(BrandGroup group, IReadOnlyList<string>? additional)
    {
        var candidates = new List<AliasCandidate>
        {
            new(string.Empty, group.DisplayName, group.Manual is null ? AliasPriority.Identity : AliasPriority.Explicit, true),
            new(string.Empty, group.Id, AliasPriority.Identity, false)
        };
        if (group.Manual is not null)
            candidates.AddRange(group.Manual.Aliases.Select(x => new AliasCandidate(
                string.Empty, x, AliasPriority.Explicit, x.Equals(group.DisplayName, StringComparison.Ordinal))));
        foreach (var record in group.Records.OrderBy(x => x.Provider, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal))
        {
            candidates.Add(new AliasCandidate(string.Empty, record.DisplayName, AliasPriority.Identity,
                record.DisplayName.Equals(group.DisplayName, StringComparison.Ordinal)));
            candidates.Add(new AliasCandidate(string.Empty, record.SourceId, AliasPriority.Identity, false));
            candidates.AddRange(record.Aliases.Select(x => new AliasCandidate(
                string.Empty, x, AliasPriority.Inferred, x.Equals(group.DisplayName, StringComparison.Ordinal))));
        }
        if (additional is not null)
            candidates.AddRange(additional.Select(x => new AliasCandidate(string.Empty, x, AliasPriority.Explicit, false)));
        return candidates.Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToArray();
    }

    private static AliasResolution ResolveAliases(
        IReadOnlyDictionary<string, List<AliasCandidate>> candidates,
        IEnumerable<string> brandIds)
    {
        var conflicts = new List<BuildConflict>();
        var aliasesByBrand = brandIds.ToDictionary(x => x, _ => new List<(string Key, string Value)>(), StringComparer.Ordinal);
        var index = new List<IssuerAliasEntry>();
        var suppressed = 0;
        foreach (var claim in candidates.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var bestPriority = claim.Value.Min(x => x.Priority);
            var strongest = claim.Value.Where(x => x.Priority == bestPriority).ToArray();
            var strongestBrands = strongest.Select(x => x.BrandId).Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (strongestBrands.Length > 1)
            {
                if (bestPriority == AliasPriority.Inferred)
                {
                    suppressed++;
                    continue;
                }
                conflicts.Add(new BuildConflict("alias-collision", claim.Key,
                    $"Normalized issuer alias '{claim.Key}' is claimed by multiple canonical brands at equal confidence.",
                    strongestBrands));
                continue;
            }

            var winner = strongestBrands[0];
            if (claim.Value.Any(x => !x.BrandId.Equals(winner, StringComparison.Ordinal))) suppressed++;
            var original = claim.Value.Where(x => x.BrandId.Equals(winner, StringComparison.Ordinal))
                .OrderBy(x => x.Priority)
                .ThenByDescending(x => x.PreferredDisplay)
                .ThenBy(x => x.Value, StringComparer.Ordinal)
                .First().Value;
            aliasesByBrand[winner].Add((claim.Key, original));
            index.Add(new IssuerAliasEntry(claim.Key, winner));
        }

        return new AliasResolution(
            index,
            aliasesByBrand.ToDictionary(x => x.Key,
                x => (IReadOnlyList<string>)x.Value.OrderBy(y => y.Key, StringComparer.Ordinal)
                    .ThenBy(y => y.Value, StringComparer.Ordinal).Select(y => y.Value).ToArray(),
                StringComparer.Ordinal),
            conflicts.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            suppressed);
    }

    private static IReadOnlyDictionary<string, string?> SortedMetadata(IReadOnlyDictionary<string, string?> metadata)
        => metadata.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string?> WithRightsMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        RightsAssessment assessment)
    {
        var result = metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        AddRightsMetadata(result, assessment);
        return result;
    }

    private static void AddRightsMetadata(IDictionary<string, string?> metadata, RightsAssessment assessment)
    {
        metadata["rightsStatus"] = RightsAssessor.ToMetadataValue(assessment.Status);
        metadata["rightsBasis"] = assessment.Basis;
        metadata["rightsLicenseType"] = assessment.LicenseType;
        metadata["rightsEvidenceUrl"] = assessment.EvidenceUrl;
        metadata["rightsNote"] = assessment.Note;
    }

    private static string Key(string provider, string sourceId) => provider + "/" + sourceId;

    private sealed class BrandGroup(string id, string displayName, CanonicalBrandMapping? manual)
    {
        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public CanonicalBrandMapping? Manual { get; } = manual;
        public List<SourceRecord> Records { get; } = [];
    }

    private enum AliasPriority
    {
        Explicit = 0,
        Identity = 1,
        Inferred = 2
    }

    private sealed record AliasCandidate(
        string BrandId,
        string Value,
        AliasPriority Priority,
        bool PreferredDisplay);

    private sealed record PendingBrand(
        string Id,
        string DisplayName,
        string BackgroundColor,
        string Icon,
        SelectedSource SelectedSource,
        IReadOnlyList<SourceReference> Sources);

    private sealed record NormalizedSelection(SourceRecord Record, SvgNormalizationResult Normalized);

    private sealed record AliasResolution(
        IReadOnlyList<IssuerAliasEntry> Index,
        IReadOnlyDictionary<string, IReadOnlyList<string>> AliasesByBrand,
        IReadOnlyList<BuildConflict> Conflicts,
        int SuppressedAliases);
}
