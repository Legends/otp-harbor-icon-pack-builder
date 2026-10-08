# OTP Harbor `.otphicons` integration guide

## Audience and boundary

This is the implementation handoff for a Codex agent adding unified-pack import support to the OTP Harbor application in `E:\Repos\TOTP-Manager`.

The user runs OTP Harbor Icon Pack Builder separately. The builder downloads Aegis Icons, Simple Icons, and Dashboard Icons, resolves them into one canonical catalog, and produces one `.otphicons` file. OTP Harbor only imports that local file.

Do not run the builder from OTP Harbor. Do not add provider downloads, update checks, CDN calls, or any other icon-resolution network dependency to OTP Harbor.

```text
User runs builder (network allowed)
          |
          v
otp-harbor-icons.otphicons
          |
          v
User imports local file into OTP Harbor (no network)
```

The normative metadata schema is `schemas/otp-harbor-icon-pack.schema.json` in this repository. The current format version is `1`.

## Future user-run import workflow

For every future catalog refresh, the user—not OTP Harbor—performs this sequence:

1. Run the builder normally (for example, press F5 in Visual Studio or execute `dotnet run --project .\src\OtpHarbor.IconPackBuilder`).
2. Let it acquire or reuse the pinned Aegis, Dashboard Icons, and Simple Icons inputs, normalize the complete catalog, and finish the zero-unresolved visual gate.
3. Keep the emitted `.visual-report.json` beside the pack for diagnostics and confirm `unresolvedFailures` is `0`; this report is not an OTP Harbor import input.
4. In OTP Harbor, choose the generated `otp-harbor-icons.otphicons` file through the local icon-pack import UI.
5. OTP Harbor validates and installs that one provider-neutral pack transactionally. It replaces the prior installed `otp-harbor-icons` catalog only after the new pack fully validates.

The user does not copy provider ZIPs, cache directories, skipped-SVG diagnostics, or the visual report into OTP Harbor. Future imports use the same format-v1 boundary until this document and both repositories explicitly add support for a later `formatVersion`.

## Current OTP Harbor integration points

Inspect these files before implementing because they are the current application-side authority:

- `TOTP.Core/Icons/IIconPackImporter.cs`
- `TOTP.Core/Icons/IconImportModels.cs`
- `TOTP.Core/Services/Models/BrandIconPackModels.cs`
- `TOTP.Infrastructure/Icons/IconPackImporterResolver.cs`
- `TOTP.Infrastructure/Icons/IconImportArchive.cs`
- `TOTP.Infrastructure/Branding/IssuerAliasResolver.cs`
- `TOTP.Infrastructure/Services/SimpleIconsBrandIconPackService.cs`
- corresponding tests under `TOTP.Tests/Services/`

The natural entry point is a dedicated `OtpHarborIconPackImporter : IIconPackImporter`, registered before the generic filename-indexed importer. Detection must inspect `pack.json`; the filename extension alone is not a trust boundary.

Add an explicit `BrandIconPackFormat.OtpHarbor` value rather than presenting this format as Simple Icons, Aegis, or a generic filename-indexed pack.

## Important incompatibilities to address

Do not merely feed this archive through the existing generic importer. Several current OTP Harbor assumptions are intentionally different from the unified format:

1. Canonical IDs in `.otphicons` are lowercase kebab-case. OTP Harbor's current `SafeSlugRegex` and `SafeSvgNameRegex` accept underscores but not hyphens. Add format-compatible validation such as `^[a-z0-9]+(?:-[a-z0-9]+)*$` and the corresponding `.svg` form.
2. The existing `IssuerAliasResolver.Normalize` removes all symbols. Format v1 preserves identity-bearing symbols such as `+` and `&`. Runtime lookup for a unified pack must use the format-v1 normalization described below, or consume a persisted pre-normalized index produced from that exact algorithm.
3. The current installation pipeline truncates each brand to 32 aliases. A unified brand can contain aliases merged from three providers and manual mappings. Do not silently truncate aliases needed for resolution; enforce a documented safe upper bound and reject an excessive pack instead.
4. The current installed `BrandIndex` does not preserve upstream provenance, selected-source metadata, pack sources, or the authoritative normalized alias index. Extend the installed model, or persist a sidecar manifest, so an installed pack remains auditable.
5. The existing service can combine separately installed upstream providers. A unified `.otphicons` file is already the resolved result and should be installed as one provider/catalog (`otp-harbor-icons`), not split back into three application providers.

These are application changes in `TOTP-Manager`; they must not be worked around by weakening the builder format.

## Container layout

`.otphicons` is a ZIP-compatible container with a custom extension:

```text
pack.json
icons/
  amazon.svg
  amazon-web-services.svg
  github.svg
licenses/
  aegis/
  dashboard-icons/
  simple-icons/
```

The builder emits forward-slash paths, ordinally sorted entries, fixed ZIP timestamps, and no local absolute paths or build timestamps. Consumers should not depend on physical ZIP entry order even though it is deterministic.

There must be exactly one root `pack.json`. Every `brand.icon` and every `source.licenseFiles` value is a container-relative path. No file may be resolved outside the archive.

## `pack.json` model

The JSON uses camel-case property names. Null source fields are emitted explicitly.

```json
{
  "formatVersion": 1,
  "packId": "otp-harbor-icons",
  "name": "OTP Harbor Icons",
  "sources": [
    {
      "provider": "simple-icons",
      "inputFileName": "source.zip",
      "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
      "version": "16.33.0",
      "revision": null,
      "sourceUrl": "https://github.com/simple-icons/simple-icons/releases/tag/16.33.0",
      "metadata": { "version": "16.33.0" },
      "licenseFiles": ["licenses/simple-icons/license.md"]
    }
  ],
  "brands": [
    {
      "id": "amazon-web-services",
      "displayName": "Amazon Web Services",
      "backgroundColor": "#232F3E",
      "icon": "icons/amazon-web-services.svg",
      "issuerAliases": ["Amazon Web Services", "Amazon AWS", "AWS"],
      "selectedSource": {
        "provider": "simple-icons",
        "sourceId": "amazonwebservices",
        "metadata": { "nativeBrandColor": "#232F3E" }
      },
      "sources": [
        {
          "provider": "simple-icons",
          "sourceId": "amazonwebservices",
          "metadata": { "nativeBrandColor": "#232F3E" }
        }
      ]
    }
  ],
  "issuerAliases": [
    { "key": "amazon aws", "brandId": "amazon-web-services" },
    { "key": "amazon web services", "brandId": "amazon-web-services" },
    { "key": "aws", "brandId": "amazon-web-services" }
  ]
}
```

### Pack sources

`sources` is pack-level acquisition provenance. It records the exact release version or commit revision where available, the upstream source page, the SHA-256 of the normalized input archive consumed by the builder, provider metadata, and copied license files.

Do not interpret `inputFileName` as a stable identity. Cached automatic inputs are normally named `source.zip`. Use `provider`, `version`, `revision`, `sourceUrl`, and `sha256` for audit displays.

The format currently has no separate pack release number. If the existing OTP Harbor import API requires a `Version` string, derive a deterministic display value from the ordered source versions/revisions (shortening a commit SHA only for display), or use `format-1`. OTP Harbor's existing archive hash already distinguishes installed content. Never use the current time.

### Brands

Each brand is already canonicalized. OTP Harbor must use `brand.id` directly and must not recanonicalize it from a provider slug or display name.

- `id`: stable OTP Harbor-owned ID.
- `displayName`: user-facing name.
- `backgroundColor`: required uppercase six-digit RGB color, including `#`.
- `icon`: selected SVG path in the archive.
- `issuerAliases`: original, human-readable aliases retained for diagnostics and display.
- `selectedSource`: provider/source record whose SVG was selected.
- `sources`: all provider records merged into the canonical brand.

`selectedSource` must match one entry in `sources`. Provenance metadata is descriptive and should not drive a second selection inside OTP Harbor; the builder has already selected the winner.

### Alias index

The top-level `issuerAliases` array is the authoritative normalized issuer lookup index. Every `key` maps to exactly one canonical `brandId`. A builder build fails before producing a pack if a normalized key is ambiguous.

The builder ranks alias evidence: explicit mappings, exact brand/source identities, then inferred provider aliases. A lower-confidence provider alias cannot override an exact identity. Broad descriptive provider aliases claimed by multiple brands are conservatively suppressed and reported in the build summary; they are absent from both the top-level index and brand alias arrays. Equal-confidence explicit or identity collisions remain fatal build conflicts.

The nested `brand.issuerAliases` values are original aliases. Preserve them, but do not confuse them with normalized lookup keys.

The optional TOTP account name must not participate in automatic brand resolution. OTP Harbor's `Account.Issuer` is mandatory and is the sole automatic input.

## Format-v1 issuer normalization

Runtime issuer lookup must produce exactly the same key as the builder:

1. trim leading/trailing whitespace;
2. apply Unicode normalization form KC (`NormalizationForm.FormKC`);
3. apply invariant lowercase folding;
4. iterate Unicode runes;
5. retain Unicode letters and decimal digits;
6. retain identity-bearing math, currency, and other symbols, including `+` and `&`;
7. convert whitespace, connector/dash/open/close/quote/other punctuation, and common separators (`- _ . / \ :`) into one pending ASCII space;
8. emit a pending space only between retained characters, never at the beginning or end.

The exact reference implementation is `src/OtpHarbor.IconPackBuilder/Normalization/IssuerNormalizer.cs` in this repository. Copy compatible behavior into OTP Harbor without adding a project reference between repositories. Add shared conformance vectors to both repositories if this algorithm evolves.

Resolution is then:

```text
normalized = FormatV1Normalize(account.Issuer)
brandId = installedPack.IssuerAliases[normalized]
brand = installedPack.Brands[brandId]
```

Do not fall back to fuzzy matching. A missing match is safer than a wrong icon.

## Recommended import transaction

Use OTP Harbor's existing staging and atomic-registration pattern:

1. Copy the selected stream to a bounded temporary archive while computing its SHA-256.
2. Open it with the protections already centralized in `IconImportArchive`.
3. Detect exactly one root `pack.json`, parse bounded JSON, and require `formatVersion == 1`.
4. Validate the complete metadata graph before installing anything.
5. Read and validate every referenced SVG as data, using the existing DTD/external-reference/script rejection and the format-v1 1 MiB SVG limit.
6. Read only referenced license files, with bounded per-file and total sizes.
7. Write the normalized installed representation into a private staging directory.
8. Reopen/reload the staged representation and validate that every alias and asset resolves.
9. Atomically move the staging directory into the packs area and atomically update the installed-pack registry.
10. On any error or cancellation, remove temporary/staging data and leave the last known-good installed pack and registry untouched.

For a unified pack, replacing a previously installed `otp-harbor-icons` registration is expected. The archive-content hash should still produce a distinct immutable storage directory, following the current service design.

## Required validation

At minimum, reject the import when any of these checks fails:

- archive path is absolute, contains `..`, contains NUL, or conflicts case-insensitively;
- compressed size, total expanded size, entry count, per-entry size, or compression ratio exceeds the application's limits;
- `pack.json` is absent, duplicated, malformed, too deep, too large, or has an unsupported version;
- arrays exceed documented application limits;
- source provider IDs are duplicated or malformed;
- source SHA-256 is not 64 lowercase hexadecimal characters;
- canonical brand IDs are duplicated or not lowercase kebab-case;
- icon paths are duplicated, non-canonical, missing, or do not agree with the brand ID convention;
- an SVG is empty, oversized, malformed, lacks supported vector paths, or contains executable/external content;
- a background color is not `#[0-9A-F]{6}`;
- a brand has no aliases or source references;
- `selectedSource` is absent from that brand's `sources`;
- a normalized alias key is duplicated, empty, non-normalized, or points to an unknown brand;
- normalizing any original brand alias does not yield an index entry for that same brand;
- a license path is unsafe, missing, duplicated, or oversized.

Do not silently skip an invalid canonical brand or alias. The builder output is an all-or-nothing resolved catalog; partial import would change its conflict and lookup semantics.

## Background color and SVG behavior

`backgroundColor` is already resolved by the builder using this order:

1. explicit canonical mapping color;
2. native metadata from the selected source;
3. a safe full-logo circle or rectangle from the selected SVG, when promotion remains visually equivalent;
4. a contrast-checked neutral fallback (`#FFFFFF` when safe for dark/colored artwork, otherwise `#334155`).

Use the supplied canonical value. OTP Harbor may validate the SVG, but it must not reinterpret provider CSS, redo source selection, or replace the supplied value with color metadata from another contributing source. The builder has already compared the final SVG and background together at the application tile size.

The builder emits `<pack-name>.visual-report.json` outside the archive. It records per-brand source/normalized palettes and pixel metrics and must state `unresolvedFailures: 0`. This is a build/release diagnostic, not part of format v1; OTP Harbor neither requires nor imports it.

The existing OTP Harbor SVG parser/rendering path remains appropriate after archive validation. SVGs are data; never execute script, load external resources, or render them during import validation.

## Provenance and licenses in the installed representation

Preserve at least:

- complete pack-level `sources`;
- archive SHA-256 calculated by OTP Harbor;
- each brand's `selectedSource` and contributing `sources`;
- copied license/notice files;
- original issuer aliases;
- normalized alias index;
- format version, pack ID, and display name.

This metadata is useful for diagnostics and an eventual “About this icon” or pack-details view. It must not contain account issuers, account names, secrets, or other user data.

## Suggested application tests

Add synthetic, offline tests for:

- format detection and successful format-v1 import;
- hyphenated canonical IDs and SVG filenames;
- exact issuer-only lookup (account name ignored);
- `+`/`&`, Unicode KC, punctuation, case, and whitespace normalization conformance;
- all aliases retained without a silent 32-item truncation;
- selected source and background color preservation;
- source provenance and license persistence;
- missing/duplicate assets and invalid references;
- alias-index collisions and unknown brand IDs;
- unsupported `formatVersion`;
- malformed/unsafe SVG and all existing ZIP security cases;
- cancellation;
- atomic replacement and preservation of the previous valid pack after failure;
- coexistence policy with legacy Aegis/Simple Icons packs, if legacy imports remain supported;
- no network access during import or runtime resolution.

Use small synthetic fixtures. Do not copy a complete third-party icon collection into OTP Harbor tests.

## Compatibility ownership

Format changes originate in this builder repository. For incompatible changes, add a new numeric `formatVersion`, update the JSON schema and builder reader/writer tests, then add explicit application support. OTP Harbor should reject unsupported versions cleanly and retain the previously installed pack.

The repositories remain assembly-independent. Share contracts through this documented file format and mirrored conformance tests, not project references.
