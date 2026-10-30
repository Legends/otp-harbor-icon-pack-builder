using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using OtpHarbor.IconPackBuilder.Normalization;
using OtpHarbor.IconPackBuilder.Providers;

namespace OtpHarbor.IconPackBuilder.Resolution;

public sealed record CatalogBuildProgress(int CompletedBrands, int TotalBrands, string CurrentBrandId);

public sealed class BrandMerger
{
    public static readonly IReadOnlyList<string> DefaultProviderPreference = ["dashboard-icons", "aegis", "simple-icons"];

    public PackBuildResult Merge(
        IReadOnlyList<ProviderCatalog> catalogs,
        MappingSet mappings,
        IReadOnlyList<string>? providerPreference = null,
        string packId = "otp-harbor-icons",
        string packName = "OTP Harbor Icons",
        Action<CatalogBuildProgress>? progress = null,
        RightsPolicy rightsPolicy = RightsPolicy.Preserve,
        int formatVersion = 1,
        CancellationToken cancellationToken = default,
        int? maxDegreeOfParallelism = null)
    {
        if (catalogs.Count == 0) throw new InputValidationException("At least one provider input is required.");
        if (formatVersion is not (1 or 2 or 3))
            throw new InputValidationException($"Unsupported output format version {formatVersion}.");
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

        var orderedGroups = groups.Values.ToArray();
        var themedSelections = formatVersion == 3
            ? PreselectThemeAppearances(orderedGroups, effectiveSourceOverrides,
                mappings.EffectiveAppearanceCompositions, providerPreference, progress,
                maxDegreeOfParallelism, cancellationToken)
            : null;

        var pendingBrands = new List<PendingBrand>();
        var icons = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var runtimeIcons = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var aliasCandidates = new Dictionary<string, List<AliasCandidate>>(StringComparer.Ordinal);
        var selectedCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var rightsCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var rightsAssessments = new List<RightsAssessmentEntry>();
        var visualVerification = new List<VisualVerificationEntry>();
        var runtimeVisualReferences = new List<RuntimeVisualReference>();
        var completedBrands = 0;

        for (var groupIndex = 0; groupIndex < orderedGroups.Length; groupIndex++)
        {
            var group = orderedGroups[groupIndex];
            cancellationToken.ThrowIfCancellationRequested();
            if (formatVersion != 3)
                progress?.Invoke(new CatalogBuildProgress(completedBrands, groups.Count, group.Id));
            var themed = formatVersion == 3
                ? themedSelections![groupIndex]
                : null;
            var selected = themed is null
                ? SelectSource(group, effectiveSourceOverrides, providerPreference, formatVersion, cancellationToken)
                : null;
            var selectedRecord = themed?.Record ?? selected!.Record;
            var selectedRights = RightsAssessor.Assess(selectedRecord, mappings.EffectiveRightsOverrides);
            if (rightsPolicy == RightsPolicy.RequireDocumented && !RightsAssessor.IsDocumented(selectedRights))
                throw new InputValidationException($"Brand '{group.Id}' selected '{selectedRecord.Provider}/{selectedRecord.SourceId}' with rights status '{RightsAssessor.ToMetadataValue(selectedRights.Status)}'. Add reviewed evidence in rights-assessments.json, choose another source, or use --rights-policy preserve.");
            var iconPath = $"icons/{group.Id}.svg";
            if (selected is not null) icons.Add(iconPath, selected.Normalized.Svg);
            RuntimeIcon? runtimeIcon = null;
            ThemeAppearances? appearances = null;
            if (themed is not null)
            {
                var light = AddThemeAsset(themed.LightTheme);
                var dark = AddThemeAsset(themed.DarkTheme);
                appearances = new ThemeAppearances(light, dark);
            }
            else if (selected!.Runtime is not null)
            {
                var runtimePath = $"runtime/foreground-128/{group.Id}.png";
                runtimeIcons.Add(runtimePath, selected.Runtime.Png);
                runtimeIcon = new RuntimeIcon(runtimePath, RuntimeIconRenderer.MediaType,
                    RuntimeIconRenderer.Width, RuntimeIconRenderer.Height,
                    selected.Runtime.Sha256, RuntimeIconRenderer.RenderProfile);
            }
            selectedCounts[selectedRecord.Provider] = selectedCounts.GetValueOrDefault(selectedRecord.Provider) + 1;
            var rightsStatus = RightsAssessor.ToMetadataValue(selectedRights.Status);
            rightsCounts[rightsStatus] = rightsCounts.GetValueOrDefault(rightsStatus) + 1;
            rightsAssessments.Add(new RightsAssessmentEntry(group.Id, selectedRecord.Provider,
                selectedRecord.SourceId, selectedRights.Status, selectedRights.Basis,
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
            var selectedMetadata = selectedRecord.Metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            AddRightsMetadata(selectedMetadata, selectedRights);
            if (themed is null)
            {
                selectedMetadata["visualNormalization"] = selected!.Normalized.Operations.Count == 0
                    ? "canonical-pass-through"
                    : string.Join(',', selected.Normalized.Operations);
                selectedMetadata["normalizedForegroundColorCount"] = selected.Normalized.ForegroundColors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                pendingBrands.Add(new PendingBrand(group.Id, group.DisplayName, selected.Normalized.BackgroundColor, iconPath,
                    new SelectedSource(selectedRecord.Provider, selectedRecord.SourceId, SortedMetadata(selectedMetadata)),
                    sourceReferences, runtimeIcon, null));
                var metrics = selected.Runtime?.VisualMetrics ?? selected.Normalized.VisualMetrics;
                var operations = selected.Runtime is null
                    ? selected.Normalized.Operations
                    : selected.Normalized.Operations.Append("rendered-runtime-foreground-v1").ToArray();
                visualVerification.Add(new VisualVerificationEntry(
                    group.Id, selectedRecord.Provider, selectedRecord.SourceId, selectedRecord.AssetPath,
                    selected.Normalized.BackgroundColor, operations, metrics.MeanAbsoluteChannelDifference,
                    metrics.MateriallyDifferentPixels, metrics.TotalPixels, metrics.SourcePalette,
                    metrics.NormalizedPalette, metrics.Resolved));
            }
            else
            {
                selectedMetadata["appearanceSelection"] = "theme-specific-v1";
                pendingBrands.Add(new PendingBrand(group.Id, group.DisplayName, null, iconPath,
                    new SelectedSource(selectedRecord.Provider, selectedRecord.SourceId, SortedMetadata(selectedMetadata)),
                    sourceReferences, null, appearances));
                AddVisualEntry("lightTheme", themed.LightTheme);
                AddVisualEntry("darkTheme", themed.DarkTheme);
            }
            completedBrands++;
            if (formatVersion != 3)
                progress?.Invoke(new CatalogBuildProgress(completedBrands, groups.Count, group.Id));

            ThemeAppearance AddThemeAsset(ThemeAppearanceSelection appearance)
            {
                var path = $"runtime/appearance-128/{appearance.Runtime.Sha256}.png";
                if (runtimeIcons.TryGetValue(path, out var existing)
                    && !existing.AsSpan().SequenceEqual(appearance.Runtime.Png))
                    throw new InputValidationException($"Runtime asset hash collision at '{path}'.");
                runtimeIcons[path] = appearance.Runtime.Png;
                runtimeVisualReferences.Add(new RuntimeVisualReference(group.Id, appearance.Theme, path,
                    appearance.Runtime.Sha256, appearance.Normalized.Svg, appearance.PreviewBackgroundColor,
                    appearance.CompositionMode, appearance.CompositionColor,
                    appearance.VisualSourceSvg));
                return new ThemeAppearance(
                    new RuntimeIcon(path, RuntimeIconRenderer.MediaType, RuntimeIconRenderer.Width,
                        RuntimeIconRenderer.Height, appearance.Runtime.Sha256, RuntimeIconRenderer.ThemeRenderProfile),
                    appearance.AssetRole,
                    appearance.AssetRole == "complete-icon"
                        ? new AppearanceBackground("embedded")
                        : new AppearanceBackground("solid", appearance.PreviewBackgroundColor),
                    appearance.Selection,
                    new AppearanceSource(appearance.Record.Provider, appearance.SourceId, appearance.AssetPath),
                    appearance.Reason);
            }

            void AddVisualEntry(string name, ThemeAppearanceSelection appearance)
            {
                var metrics = appearance.Runtime.VisualMetrics;
                visualVerification.Add(new VisualVerificationEntry(
                    group.Id, appearance.Record.Provider, appearance.SourceId, appearance.AssetPath,
                    appearance.PreviewBackgroundColor,
                    appearance.Normalized.Operations.Append("rendered-runtime-themed-v1").ToArray(),
                    metrics.MeanAbsoluteChannelDifference, metrics.MateriallyDifferentPixels, metrics.TotalPixels,
                    metrics.SourcePalette, metrics.NormalizedPalette, metrics.Resolved,
                    name, appearance.AssetRole, appearance.Reason));
            }
        }

        var aliasResolution = ResolveAliases(aliasCandidates, pendingBrands.Select(x => x.Id));
        if (aliasResolution.Conflicts.Count > 0) throw new BuildConflictException(aliasResolution.Conflicts);
        var outputBrands = pendingBrands.Select(brand => new PackBrand(
                brand.Id,
                brand.DisplayName,
                brand.BackgroundColor,
                formatVersion == 1 ? brand.Icon : null,
                aliasResolution.AliasesByBrand[brand.Id],
                brand.SelectedSource,
                brand.Sources,
                brand.RuntimeIcon,
                brand.Appearances))
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
            FormatVersion = formatVersion,
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
            rightsAssessments.OrderBy(x => x.BrandId, StringComparer.Ordinal).ToArray(),
            runtimeIcons,
            runtimeVisualReferences);
    }

    private static ThemeSelection[] PreselectThemeAppearances(
        IReadOnlyList<BrandGroup> groups,
        IReadOnlyDictionary<string, SourceOverride> overrides,
        IReadOnlyDictionary<string, AppearanceCompositionOverride> compositions,
        IReadOnlyList<string> preference,
        Action<CatalogBuildProgress>? progress,
        int? requestedDegree,
        CancellationToken cancellationToken)
    {
        if (requestedDegree is <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedDegree));
        // Rasterization, compression, and GC can use helper threads beyond the
        // Parallel.For workers. Limit foreground workers to 37.5% of logical
        // processors so total builder CPU remains below the 80% product ceiling
        // with headroom for those helpers and for the rest of the machine.
        var degree = requestedDegree ?? Math.Max(1, Environment.ProcessorCount * 3 / 8);
        var selections = new ThemeSelection?[groups.Count];
        var failures = new Exception?[groups.Count];
        var completed = 0;
        var progressLock = new object();
        var cpuThrottle = new CpuLoadThrottle();
        if (groups.Count > 0)
            progress?.Invoke(new CatalogBuildProgress(0, groups.Count, groups[0].Id));
        Parallel.For(0, groups.Count, new ParallelOptions
        {
            MaxDegreeOfParallelism = degree,
            CancellationToken = cancellationToken
        }, index =>
        {
            cpuThrottle.Wait(cancellationToken);
            var group = groups[index];
            try
            {
                selections[index] = SelectThemeAppearances(group, overrides,
                    compositions.GetValueOrDefault(group.Id), preference, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures[index] = ex;
            }
            finally
            {
                var current = Interlocked.Increment(ref completed);
                if (progress is not null)
                    lock (progressLock)
                        progress(new CatalogBuildProgress(current, groups.Count, group.Id));
            }
        });
        var firstFailure = Array.FindIndex(failures, failure => failure is not null);
        if (firstFailure >= 0)
            ExceptionDispatchInfo.Capture(failures[firstFailure]!).Throw();
        return selections.Select(selection => selection!).ToArray();
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

    private static NormalizedSelection SelectSource(
        BrandGroup group,
        IReadOnlyDictionary<string, SourceOverride> overrides,
        IReadOnlyList<string> preference,
        int formatVersion,
        CancellationToken cancellationToken)
    {
        if (overrides.TryGetValue(group.Id, out var sourceOverride))
        {
            var overridden = group.Records.SingleOrDefault(x => x.Provider.Equals(sourceOverride.Provider, StringComparison.OrdinalIgnoreCase)
                && x.SourceId.Equals(sourceOverride.SourceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InputValidationException($"Source override for '{group.Id}' refers to missing record '{sourceOverride.Provider}/{sourceOverride.SourceId}'.");
            var normalizedOverride = Normalize(overridden, ResolveBackgroundColor(group, overridden), group.Id,
                materializeAegisSvgDefaults: formatVersion == 2);
            return formatVersion == 2
                ? RenderRuntime(normalizedOverride, group.Id, cancellationToken)
                : normalizedOverride;
        }

        var failures = new List<string>();
        var normalized = new List<NormalizedSelection>();
        foreach (var record in group.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                normalized.Add(Normalize(record, ResolveBackgroundColor(group, record), group.Id,
                    materializeAegisSvgDefaults: formatVersion == 2));
            }
            catch (InputValidationException ex) { failures.Add($"{record.Provider}/{record.SourceId}: {ex.Message}"); }
        }
        if (normalized.Count == 0)
            throw new InputValidationException($"Brand '{group.Id}' has no source that can be normalized: {string.Join("; ", failures)}");
        var ranked = normalized.OrderBy(x => PreferenceRank(x.Record.Provider, preference))
            .ThenBy(VisualQualityRank)
            .ThenBy(x => QualityRank(x.Record.Quality)).ThenBy(x => x.Record.Provider, StringComparer.Ordinal)
            .ThenBy(x => x.Record.SourceId, StringComparer.Ordinal).ToArray();
        if (formatVersion == 1) return ranked[0];
        foreach (var candidate in ranked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return RenderRuntime(candidate, group.Id, cancellationToken); }
            catch (InputValidationException ex) { failures.Add($"{candidate.Record.Provider}/{candidate.Record.SourceId}: {ex.Message}"); }
        }
        throw new InputValidationException($"Brand '{group.Id}' has no source with a valid runtime rendering: {string.Join("; ", failures)}");
    }

    private static ThemeSelection SelectThemeAppearances(
        BrandGroup group,
        IReadOnlyDictionary<string, SourceOverride> overrides,
        AppearanceCompositionOverride? composition,
        IReadOnlyList<string> preference,
        CancellationToken cancellationToken)
    {
        IEnumerable<SourceRecord> records = group.Records;
        var sourceOverride = overrides.GetValueOrDefault(group.Id);
        if (composition is not null)
        {
            if (sourceOverride is not null
                && (!sourceOverride.Provider.Equals(composition.Provider, StringComparison.OrdinalIgnoreCase)
                    || !sourceOverride.SourceId.Equals(composition.SourceId, StringComparison.OrdinalIgnoreCase)))
                throw new InputValidationException($"Appearance composition for '{group.Id}' conflicts with its source override.");
            if (sourceOverride?.LightForegroundColor is not null
                || sourceOverride?.DarkForegroundColor is not null)
                throw new InputValidationException(
                    $"Appearance composition for '{group.Id}' cannot also define reviewed theme foreground colors.");
            var composed = group.Records.SingleOrDefault(record =>
                record.Provider.Equals(composition.Provider, StringComparison.OrdinalIgnoreCase)
                && record.SourceId.Equals(composition.SourceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InputValidationException($"Appearance composition for '{group.Id}' refers to missing record '{composition.Provider}/{composition.SourceId}'.");
            records = [composed];
        }
        else if (sourceOverride is not null)
        {
            var overridden = group.Records.SingleOrDefault(record =>
                record.Provider.Equals(sourceOverride.Provider, StringComparison.OrdinalIgnoreCase)
                && record.SourceId.Equals(sourceOverride.SourceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InputValidationException($"Source override for '{group.Id}' refers to missing record '{sourceOverride.Provider}/{sourceOverride.SourceId}'.");
            records = [overridden];
        }

        // Rank with source metadata and static SVG paint information first.
        // Full normalization/rasterization is deliberately performed only for
        // the first usable source. The old v3 implementation rendered every
        // source twice before selecting one, which multiplied full-catalog
        // build time without improving validation of the packaged winner.
        var rankedRecords = records
            .OrderBy(record => PreferenceRank(record.Provider, preference))
            .ThenBy(StaticVisualQualityRank)
            .ThenBy(record => QualityRank(record.Quality))
            .ThenBy(record => record.Provider, StringComparer.Ordinal)
            .ThenBy(record => record.SourceId, StringComparer.Ordinal)
            .ToArray();
        var failures = new List<string>();
        foreach (var record in rankedRecords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var workCache = new ThemeWorkCache();
                return new ThemeSelection(
                    record,
                    SelectThemeAppearance(record, group.Id, "lightTheme", group.Manual?.BackgroundColor,
                        composition, sourceOverride, workCache, cancellationToken),
                    SelectThemeAppearance(record, group.Id, "darkTheme", group.Manual?.BackgroundColor,
                        composition, sourceOverride, workCache, cancellationToken));
            }
            catch (InputValidationException ex)
            {
                failures.Add($"{record.Provider}/{record.SourceId}: {ex.Message}");
            }
        }
        throw new InputValidationException($"Brand '{group.Id}' has no source with valid themed appearances: {string.Join("; ", failures)}");
    }

    private static ThemeAppearanceSelection SelectThemeAppearance(
        SourceRecord record,
        string brandId,
        string theme,
        string? explicitBackground,
        AppearanceCompositionOverride? composition,
        SourceOverride? sourceOverride,
        ThemeWorkCache workCache,
        CancellationToken cancellationToken)
    {
        var sourceVariants = (composition is null ? record.Variants ?? [] : [])
            .Where(variant => IsVariantCandidate(record.Provider, variant.Kind, theme))
            .ToArray();
        var intendedVariants = sourceVariants
            .Where(variant => IsVariantEligible(record.Provider, variant.Kind, theme))
            .ToArray();
        var variants = intendedVariants
            .Select(variant => (Variant: (SourceAssetVariant?)variant, Preference: 0))
            // An opposite-theme Dashboard asset is a safe fallback only when
            // the provider actually declared an intended asset and its pixels
            // failed the surface-contrast check. If the intended relationship
            // is absent, retain the default artwork instead of guessing from
            // a lone variant.
            .Concat(intendedVariants.Length == 0
                ? []
                : sourceVariants.Where(variant => !IsVariantEligible(record.Provider, variant.Kind, theme))
                    .Select(variant => (Variant: (SourceAssetVariant?)variant, Preference: 1)))
            .OrderBy(candidate => candidate.Preference)
            .ThenBy(candidate => candidate.Variant!.SourceId, StringComparer.Ordinal)
            .Append((null, 2))
            .ToArray();
        var failures = new List<string>();
        foreach (var candidate in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var svg = candidate.Variant?.Svg ?? record.Svg;
                var sourceId = candidate.Variant?.SourceId ?? record.SourceId;
                var assetPath = candidate.Variant?.AssetPath ?? record.AssetPath;
                var themeSurface = theme == "lightTheme" ? "#FFFFFF" : "#121212";
                // Preserve unpaired artwork on a neutral light tile in either
                // app theme. Only a provider-supplied dark-theme appearance is
                // presumed suitable for a dark tile. This favors authentic
                // source paint over automatic dark-mode recoloring.
                var preferredBackground = explicitBackground
                    ?? (theme == "darkTheme" && candidate.Variant is not null ? "#121212" : "#FFFFFF");
                var reviewedThemeColor = theme == "lightTheme"
                    ? sourceOverride?.LightForegroundColor
                    : sourceOverride?.DarkForegroundColor;
                var implicitColor = reviewedThemeColor
                    ?? (record.Provider.Equals("simple-icons", StringComparison.OrdinalIgnoreCase)
                        ? record.Metadata.GetValueOrDefault("nativeBrandColor")
                        : null);
                var visualSourceSvg = implicitColor is null
                    ? svg
                    : SvgNormalizer.MaterializeInheritedFill(svg, implicitColor,
                        $"{record.Provider}/{sourceId} for '{brandId}' ({theme})");
                var normalized = workCache.Normalize(visualSourceSvg, preferredBackground,
                    materializeImplicitBlackFill: record.Provider.Equals("aegis", StringComparison.OrdinalIgnoreCase),
                    preserveEmbeddedBackground: true,
                    implicitFillColor: implicitColor,
                    preferHighResolutionRasterFallback: true,
                    useRuntimeVisualProfile: true);
                var preservedFullBackground = normalized.Operations.Contains("preserved-full-logo-background", StringComparer.Ordinal);
                var sourceBackedComposition = IsSourceBackedCompleteComposition(record, normalized);
                var visibleTransparentBrandMark = composition is null
                    && explicitBackground is null
                    && implicitColor is not null
                    && normalized.RuntimeRgba is not null
                    && RuntimeIconRenderer.HasUsableSurfaceContrast(themeSurface, normalized.RuntimeRgba);
                var complete = preservedFullBackground || sourceBackedComposition || visibleTransparentBrandMark;
                if (candidate.Variant is not null
                    && !complete
                    && normalized.RuntimeRgba is not null
                    && !RuntimeIconRenderer.HasUsableSurfaceContrast(themeSurface, normalized.RuntimeRgba))
                    throw new InputValidationException(
                        $"Provider-supplied artwork '{sourceId}' has insufficient foreground contrast on the {theme} surface '{themeSurface}'.");
                if (sourceBackedComposition && !preservedFullBackground)
                {
                    normalized = normalized with
                    {
                        Operations = normalized.Operations.Append("source-backed-complete-composition")
                            .OrderBy(operation => operation, StringComparer.Ordinal).ToArray()
                    };
                }
                if (implicitColor is not null)
                {
                    normalized = normalized with
                    {
                        Operations = normalized.Operations.Append("applied-provider-foreground-color")
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(operation => operation, StringComparer.Ordinal).ToArray()
                    };
                }
                if (reviewedThemeColor is not null)
                {
                    normalized = normalized with
                    {
                        Operations = normalized.Operations.Append("applied-reviewed-theme-foreground-color")
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(operation => operation, StringComparer.Ordinal).ToArray()
                    };
                }
                if (composition is not null)
                {
                    complete = true;
                    normalized = normalized with
                    {
                        Operations = normalized.Operations
                            .Append("reviewed-enclosed-matte-composition")
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(operation => operation, StringComparer.Ordinal).ToArray()
                    };
                }
                var role = complete ? "complete-icon" : "foreground";
                var previewBackground = complete ? explicitBackground ?? themeSurface : normalized.BackgroundColor;
                if (!complete && normalized.RuntimeRgba is not null)
                    previewBackground = RuntimeIconRenderer.EnsureVisibleBackground(
                        previewBackground, normalized.RuntimeRgba);
                var diagnostic = $"{record.Provider}/{sourceId} for '{brandId}' ({theme})";
                var requiresDirectSourceGate = normalized.RuntimeRgba is not null;
                var runtime = workCache.Render(normalized, previewBackground, diagnostic,
                    composition, requiresDirectSourceGate ? visualSourceSvg : null, cancellationToken);
                var selection = composition is not null ? "reviewed-badge-composition"
                    : candidate.Variant is not null ? "native-theme-variant"
                    : reviewedThemeColor is not null ? "brand-color-foreground"
                    : complete ? "complete-source-artwork"
                    : implicitColor is not null ? "brand-color-foreground"
                    : "source-default";
                var reason = composition is not null
                    ? composition.Rationale
                    : candidate.Variant is not null
                    ? $"Selected provider-supplied {theme} artwork '{sourceId}' after normalization and visual verification."
                    : reviewedThemeColor is not null
                        ? $"{sourceOverride!.AppearanceRationale} Applied {reviewedThemeColor} for {theme}."
                    : complete
                            ? $"Preserved the source's complete visual composition for {theme}."
                        : implicitColor is not null
                            ? $"Preserved the provider's documented primary brand color on a contrast-checked {theme} tile."
                            : $"Used the source artwork with a deterministic contrast-checked {theme} background.";
                return new ThemeAppearanceSelection(theme, record, sourceId, assetPath,
                    normalized, runtime, role, previewBackground, selection, reason,
                    composition?.Mode, composition?.MatteColor,
                    composition is null && requiresDirectSourceGate ? visualSourceSvg : null);
            }
            catch (InputValidationException ex)
            {
                failures.Add($"{candidate.Variant?.SourceId ?? record.SourceId}: {ex.Message}");
            }
        }
        throw new InputValidationException($"No usable {theme} artwork: {string.Join("; ", failures)}");
    }

    private static int StaticVisualQualityRank(SourceRecord record)
    {
        var svg = Encoding.UTF8.GetString(record.Svg);
        var colors = SourcePaintColors(record.Svg);
        var chromatic = colors.Count(IsChromatic);
        var rank = chromatic >= 2 ? 0
            : chromatic == 1 || colors.Count >= 2 ? 2
            : colors.Count == 1 ? 4
            : 5;
        // These constructs can still be selected, but normalization normally
        // has to raster-flatten them. Prefer a faithful vector candidate when
        // one is available, matching the existing quality policy cheaply.
        if (svg.Contains("<linearGradient", StringComparison.OrdinalIgnoreCase)
            || svg.Contains("<radialGradient", StringComparison.OrdinalIgnoreCase)
            || svg.Contains("<image", StringComparison.OrdinalIgnoreCase)
            || svg.Contains("<filter", StringComparison.OrdinalIgnoreCase))
            rank += 10;
        return rank;
    }

    private static bool IsSourceBackedCompleteComposition(
        SourceRecord record,
        SvgNormalizationResult normalized)
        => (record.Provider.Equals("aegis", StringComparison.OrdinalIgnoreCase)
                || record.Provider.Equals("dashboard-icons", StringComparison.OrdinalIgnoreCase))
            && normalized.ForegroundColors.Count >= 2;

    private static HashSet<string> SourcePaintColors(byte[] svgBytes)
    {
        var svg = Encoding.UTF8.GetString(svgBytes);
        var colors = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index + 6 < svg.Length; index++)
        {
            if (svg[index] != '#') continue;
            var value = svg.Substring(index, 7).ToUpperInvariant();
            if (ProviderHelpers.IsHexColor(value[1..])) colors.Add(value);
        }
        return colors;
    }

    private static bool IsVariantEligible(string provider, string kind, string theme)
    {
        if (provider.Equals("dashboard-icons", StringComparison.OrdinalIgnoreCase))
            return kind.Equals(theme == "lightTheme" ? "light-theme" : "dark-theme", StringComparison.Ordinal);
        // Aegis variations are retained as auditable source relationships, but
        // their colors are collection-specific adaptations rather than a
        // machine-readable assertion of official light/dark brand artwork.
        // Do not select them automatically without a reviewed appearance map.
        return false;
    }

    private static bool IsVariantCandidate(string provider, string kind, string theme)
    {
        if (provider.Equals("dashboard-icons", StringComparison.OrdinalIgnoreCase))
            return kind is "light-theme" or "dark-theme";
        return IsVariantEligible(provider, kind, theme);
    }

    private static NormalizedSelection Normalize(
        SourceRecord record,
        string backgroundColor,
        string brandId,
        bool materializeAegisSvgDefaults)
    {
        try
        {
            var materializeImplicitBlackFill = record.Provider.Equals("simple-icons", StringComparison.OrdinalIgnoreCase)
                    && backgroundColor.Equals("#FFFFFF", StringComparison.Ordinal)
                || materializeAegisSvgDefaults
                    && record.Provider.Equals("aegis", StringComparison.OrdinalIgnoreCase);
            var normalized = SvgNormalizer.Normalize(record.Svg, backgroundColor, materializeImplicitBlackFill);
            return new NormalizedSelection(record, normalized, null);
        }
        catch (InputValidationException ex)
        {
            throw new InputValidationException($"{record.Provider}/{record.SourceId} for '{brandId}' could not be normalized: {ex.Message}");
        }
    }

    private static NormalizedSelection RenderRuntime(
        NormalizedSelection selection,
        string brandId,
        CancellationToken cancellationToken)
    {
        try
        {
            var runtime = RuntimeIconRenderer.Render(selection.Normalized.Svg, selection.Normalized.BackgroundColor,
                $"{selection.Record.Provider}/{selection.Record.SourceId} for '{brandId}'", cancellationToken);
            return selection with { Runtime = runtime };
        }
        catch (InputValidationException ex)
        {
            throw new InputValidationException(
                $"{selection.Record.Provider}/{selection.Record.SourceId} for '{brandId}' could not be rendered: {ex.Message}");
        }
    }

    private static int VisualQualityRank(NormalizedSelection selection)
    {
        var operations = selection.Normalized.Operations;
        var usesRasterFallback = operations.Any(operation =>
            operation.StartsWith("raster-flattened-at-", StringComparison.Ordinal));
        var usesGeneratedNeutral = operations.Contains("selected-light-contrast-background", StringComparer.Ordinal)
            || operations.Contains("selected-dark-contrast-background", StringComparer.Ordinal);
        var hasSourceBackedBackground = operations.Contains("promoted-full-logo-background", StringComparer.Ordinal)
            || operations.Contains("preserved-full-logo-background", StringComparer.Ordinal)
            || (!usesGeneratedNeutral
                && !selection.Normalized.BackgroundColor.Equals("#334155", StringComparison.Ordinal));
        var colorCount = selection.Normalized.ForegroundColors.Count;
        var chromaticColorCount = selection.Normalized.ForegroundColors.Count(IsChromatic);

        // Rank visual quality within a provider. The caller applies explicit
        // provider preference first, while source overrides remain authoritative.
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
        string? BackgroundColor,
        string Icon,
        SelectedSource SelectedSource,
        IReadOnlyList<SourceReference> Sources,
        RuntimeIcon? RuntimeIcon,
        ThemeAppearances? Appearances);

    private sealed record NormalizedSelection(
        SourceRecord Record,
        SvgNormalizationResult Normalized,
        RuntimeIconRenderResult? Runtime);

    private sealed record ThemeSelection(
        SourceRecord Record,
        ThemeAppearanceSelection LightTheme,
        ThemeAppearanceSelection DarkTheme);

    private sealed record ThemeAppearanceSelection(
        string Theme,
        SourceRecord Record,
        string SourceId,
        string AssetPath,
        SvgNormalizationResult Normalized,
        RuntimeIconRenderResult Runtime,
        string AssetRole,
        string PreviewBackgroundColor,
        string Selection,
        string Reason,
        string? CompositionMode,
        string? CompositionColor,
        byte[]? VisualSourceSvg);

    private sealed class ThemeWorkCache
    {
        private readonly Dictionary<NormalizationWorkKey, SvgNormalizationResult> _normalizations = [];
        private readonly Dictionary<RuntimeWorkKey, RuntimeIconRenderResult> _renders = [];

        public SvgNormalizationResult Normalize(
            byte[] svg,
            string backgroundColor,
            bool materializeImplicitBlackFill = false,
            bool preserveEmbeddedBackground = false,
            string? implicitFillColor = null,
            bool preferHighResolutionRasterFallback = false,
            bool useRuntimeVisualProfile = false)
        {
            var key = new NormalizationWorkKey(Hash(svg), backgroundColor,
                materializeImplicitBlackFill, preserveEmbeddedBackground,
                implicitFillColor, preferHighResolutionRasterFallback, useRuntimeVisualProfile);
            if (_normalizations.TryGetValue(key, out var cached)) return cached;
            var normalized = SvgNormalizer.Normalize(svg, backgroundColor,
                materializeImplicitBlackFill, preserveEmbeddedBackground,
                implicitFillColor, preferHighResolutionRasterFallback,
                useRuntimeVisualProfile: useRuntimeVisualProfile);
            _normalizations.Add(key, normalized);
            return normalized;
        }

        public RuntimeIconRenderResult Render(
            SvgNormalizationResult normalized,
            string previewBackground,
            string diagnostic,
            AppearanceCompositionOverride? composition,
            byte[]? visualSourceSvg,
            CancellationToken cancellationToken)
        {
            var key = new RuntimeWorkKey(Hash(normalized.Svg), previewBackground,
                composition?.Mode, composition?.MatteColor,
                normalized.RuntimeRgba is null ? null : Hash(normalized.RuntimeRgba));
            if (_renders.TryGetValue(key, out var cached)) return cached;
            var rendered = composition is null
                ? RuntimeIconRenderer.RenderThemed(normalized.Svg, previewBackground, diagnostic,
                    cancellationToken, visualSourceSvg, normalized.RuntimeRgba)
                : RuntimeIconRenderer.RenderReviewedBadgeComposition(normalized.Svg,
                    composition.MatteColor, previewBackground, diagnostic, cancellationToken);
            _renders.Add(key, rendered);
            return rendered;
        }

        private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    }

    private sealed record NormalizationWorkKey(
        string SvgHash,
        string BackgroundColor,
        bool MaterializeImplicitBlackFill,
        bool PreserveEmbeddedBackground,
        string? ImplicitFillColor,
        bool PreferHighResolutionRasterFallback,
        bool UseRuntimeVisualProfile);

    private sealed record RuntimeWorkKey(
        string SvgHash,
        string PreviewBackgroundColor,
        string? CompositionMode,
        string? CompositionColor,
        string? RuntimeRgbaHash);

    private sealed record AliasResolution(
        IReadOnlyList<IssuerAliasEntry> Index,
        IReadOnlyDictionary<string, IReadOnlyList<string>> AliasesByBrand,
        IReadOnlyList<BuildConflict> Conflicts,
        int SuppressedAliases);
}
