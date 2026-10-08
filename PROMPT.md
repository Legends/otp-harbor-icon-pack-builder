Update the OTP Harbor Icon Pack Builder to use the following product workflow and architecture.

Read and follow `AGENTS.md` before making changes.

# Product goal

The OTP Harbor Icon Pack Builder must create one unified "best of all sources" icon pack by combining:

- Aegis Icons
- Simple Icons
- Dashboard Icons

The normal user must not manually download any of these provider archives.

The builder itself is allowed to access the network.

Generated packs remain fully local-first and must not need network access when consumed.

The separation is:

```text
OtpHarbor.IconPackBuilder
    |
    +-- downloads latest Aegis Icons
    +-- downloads latest Simple Icons
    +-- downloads latest Dashboard Icons
    +-- parses all three
    +-- canonicalizes brands
    +-- merges aliases
    +-- resolves duplicates/conflicts
    +-- selects the preferred icon
    +-- determines background color
    +-- produces one .otphicons file

Compatible importer
    |
    +-- imports the resulting .otphicons file locally
    +-- performs no network access for icon resolution
```

# Normal end-user workflow

Running either:

```text
OtpHarbor.IconPackBuilder
```

or:

```text
OtpHarbor.IconPackBuilder build
```

must start the normal build process.

No provider arguments must be required.

No-argument execution must NOT show help and must NOT return an input-error code.

The normal workflow must:

1. resolve the latest supported upstream version/revision of all three providers;
2. download the required metadata and SVG assets;
3. parse all three sources;
4. merge them into one canonical brand catalog;
5. validate the resulting catalog;
6. create one final `.otphicons` file;
7. validate the completed pack by reopening it before making it the final output.

# Upstream acquisition

## Aegis Icons

Automatically resolve the latest supported Aegis icon-pack release.

Prefer the official Aegis icon-pack distribution/archive intended for importing icons rather than downloading arbitrary repository contents.

Capture at least:

- upstream version or release identifier,
- source URL,
- relevant license/attribution metadata,
- pack metadata,
- primary icons,
- issuer aliases.

Use Aegis primary icons as normal brand candidates.

Do not treat generic category icons as brands.

Do not prefer variation icons over primary icons unless an explicit mapping/override requires it.

Aegis issuer values are especially valuable because they are designed around TOTP issuer matching.

## Simple Icons

Resolve the latest published Simple Icons version.

Do not blindly use an unpinned development branch.

Pin the resolved version for the complete duration of one build.

Acquire the required:

- metadata,
- SVG assets,
- title,
- slug,
- aliases,
- brand color,
- source URL,
- guidelines URL,
- license metadata.

All Simple Icons data within one generated pack must come from the same resolved version.

## Dashboard Icons

Resolve one exact Dashboard Icons upstream revision before downloading files.

Prefer an immutable commit SHA for the duration of the build.

Acquire only what the builder actually needs, especially:

- metadata,
- aliases,
- SVG assets,
- relevant license/provenance data.

Dashboard Icons identifies the repository under Apache License 2.0. Download
and preserve `LICENSE` from the same pinned commit as its metadata and SVGs,
record `Apache-2.0` and a revision-pinned license URL in the provider source
metadata, and include the license file in the generated pack. Do not imply
that this repository license grants rights to third-party trademarks depicted
by individual icons; trademark and brand-usage restrictions remain separate.

Do not download PNG/WebP or unrelated repository assets unless required.

All Dashboard Icons metadata and SVGs within one build must come from the same pinned commit.

Never mix metadata from one Dashboard Icons revision with SVGs from another revision.

# Reproducibility and provenance

At the beginning of a build, resolve concrete upstream versions/revisions.

Record them in the generated pack.

Example concept:

```json
{
  "sources": [
    {
      "provider": "aegis",
      "version": "..."
    },
    {
      "provider": "simple-icons",
      "version": "..."
    },
    {
      "provider": "dashboard-icons",
      "commit": "..."
    }
  ]
}
```

Also retain appropriate integrity/provenance information where practical.

A generated pack must be auditable: it should be possible to determine which upstream versions produced it.

Do not use floating `"latest"` references inside final source metadata when an exact release/version/commit can be recorded.

# Local cache

Downloaded upstream data must be cached in an OS-appropriate application cache location.

Do not put source downloads in the user's Downloads directory.

Use cross-platform conventions.

Conceptually:

```text
Windows:
LocalApplicationData / OTP Harbor / IconPackBuilder / cache

Linux:
XDG cache directory when available

macOS:
appropriate user cache directory
```

The exact implementation should use platform-appropriate .NET behavior.

Organize cached data by provider and immutable version/revision.

Conceptually:

```text
cache/
  aegis/
    <version>/

  simple-icons/
    <version>/

  dashboard-icons/
    <commit-sha>/
```

If the exact resolved version is already cached and valid, reuse it instead of downloading it again.

Add a mechanism such as:

```text
--refresh
```

to force upstream resolution/download again when appropriate.

Cache validation must prevent corrupt or partial downloads from being reused.

Use temporary files/directories and atomic completion markers or equivalent safe behavior.

# Offline/local-source support

Retain the existing provider source options as advanced overrides:

```text
--aegis <path>
--simple-icons <path>
--dashboard-icons <path>
```

These are no longer mandatory.

If specified, the local source must override automatic acquisition for that provider.

Also implement:

```text
--offline
```

When `--offline` is active:

- no network access is allowed;
- each provider must be available either through:
  - an explicit local override, or
  - a suitable already-cached version;
- otherwise fail with a clear diagnostic.

This functionality is primarily for development, testing, reproducibility, and air-gapped use.

# Unified pack behavior

The tool must always aim to create one merged canonical catalog from all three providers.

The product concept is:

```text
Aegis
   +
Simple Icons
   +
Dashboard Icons
   |
   v
canonical brand resolution
   |
   +-- deduplicate equivalent brands
   +-- merge issuer aliases
   +-- detect conflicts
   +-- preserve provenance
   +-- select one preferred SVG
   +-- select one canonical background color
   |
   v
otp-harbor-icons.otphicons
```

Do not expose three separate provider packs as the normal result.

# Issuer semantics

Automatic icon lookup uses the mandatory TOTP `issuer`.

The optional account name is NOT used for automatic icon matching.

The builder must therefore produce a strong issuer alias registry.

The alias system should combine useful values from:

- Aegis issuer data,
- Simple Icons names/slugs/aliases,
- Dashboard Icons names/slugs/aliases,
- manually maintained canonical mappings.

Preserve original issuer aliases in pack metadata.

Use normalized issuer keys internally.

# Canonical brand identity

Canonical brand IDs belong to the `.otphicons` format.

Do not use a provider slug as the permanent canonical ID merely because that provider wins icon selection.

Canonical IDs should remain:

- stable,
- provider-independent,
- lowercase kebab-case,
- deterministic.

Examples:

```text
amazon
amazon-web-services
microsoft
microsoft-365
github
github-actions
bosch
siemens
```

Do not merge distinct brands/products merely because names are similar.

These must stay distinct unless explicit mappings say otherwise:

```text
Amazon
Amazon Web Services

Microsoft
Microsoft 365

GitHub
GitHub Actions
```

Do not use fuzzy matching to automatically merge provider records.

# Alias conflict behavior

Normalized issuer aliases must resolve to at most one canonical brand.

If the same normalized alias is claimed by two different canonical brands:

- do not choose one arbitrarily;
- produce a build conflict;
- fail the build by default;
- write a machine-readable conflict report;
- allow the conflict to be resolved using mapping files.

Fail-on-alias-conflict remains a deliberate builder-format requirement.

# Manual mappings

Continue supporting source-controlled mappings such as:

```text
mappings/
  canonical-brands.json
  issuer-aliases.json
  source-overrides.json
```

The canonical mapping data should support explicit equivalence between provider identities and `.otphicons` canonical IDs.

Seed/retain useful canonical mappings for at least:

- Amazon
- Amazon Web Services / AWS
- Microsoft
- Microsoft 365 / Office 365 / M365
- Google
- GitHub
- Bosch
- Siemens

Do not merge Amazon with AWS.

Do not merge Microsoft with Microsoft 365.

# Icon selection

When multiple providers represent the same canonical brand, merge their metadata/aliases but select one SVG.

Selection must be deterministic.

Use the following decision structure:

1. explicit source override;
2. configured provider preference;
3. provider-specific quality rules;
4. deterministic fallback.

The initial default provider preference may remain:

```text
Aegis primary
Dashboard Icons default SVG
Simple Icons SVG
```

but keep this policy centralized and easy to change.

Never derive provider preference implicitly from provider/catalog iteration order.

The selected provider and original upstream source ID must be written into pack metadata.

# Canonical visual output and format boundary

The builder is a standalone product. Provider acquisition, parsing, source
selection, SVG normalization, color extraction, merge logic, and pack
production remain in this repository. Compatible importers only validate and
consume the provider-neutral `.otphicons` result described by the checked-in
schema and `docs/ICON-PACK-FORMAT.md`.

At minimum:

1. emit a finite positive `viewBox`, deriving it from numeric width/height when
   necessary;
2. expand local `<use>` references;
3. convert visible circles, ellipses, rectangles, polygons, and polylines to
   path geometry;
4. resolve CSS classes, inline styles, inherited paint, and transforms;
5. emit explicit safe solid path fills for colored foreground layers, while an
   intentionally omitted fill means the format's standard foreground brush;
6. flatten gradients, patterns, masks, filters, clipping, strokes, and opacity
   into supported path layers, or select a compatible alternate source rather
   than silently losing visible artwork;
7. remove a full-logo background shape in favor of `backgroundColor` only when
   the tile remains visually equivalent;
8. preserve selected-source provenance and record any deterministic visual
   normalization in metadata.

Provider priority alone is not sufficient source-quality ranking. Prefer an
alternate contributing source when the nominally preferred SVG cannot be
normalized faithfully but the alternate can. Keep that decision deterministic
and visible in `selectedSource`.

# Background and foreground colors

The pack format requires a background color for every brand.

`backgroundColor` must therefore be a first-class canonical brand property in the generated pack.

Use this deterministic precedence:

1. explicit color override, if supported;
2. native color metadata from the selected provider, e.g. Simple Icons `hex`;
3. a full-logo background shape from the actually selected SVG, including a
   safe solid `fill` supplied through a presentation attribute, inline style,
   inherited group/root paint, or resolved provider CSS;
4. fallback:

```text
#334155
```

The background color must be evaluated with the normalized foreground. It is
the final icon tile surface, not simply the first color encountered.
Explicit multicolor foreground layers must retain their selected-source colors,
and the chosen background must not hide required foreground layers.

Do not take an arbitrary color from another merged provider record when a different provider's icon was selected.

Do not copy background extraction or provider interpretation into consumers.
The builder owns this decision and writes the final value to the
provider-neutral manifest. `#334155` is a last, contrast-checked fallback; do
not use it when the selected asset contains or documents a usable background.

Add deterministic visual regression checks at the format's tile size. At minimum,
cover these known failures from the October 2026 generated pack:

- Bitdefender, Bitrise, and Bitwarden: background circle color expressed in an
  inline style;
- Bluesky: foreground color expressed in an inline path style;
- Diners Club and Firebase: non-24 view boxes and multiple colored layers;
- Ethereum: numeric width/height without a `viewBox`;
- Garmin: transformed colored path;
- Goodreads: visible rectangle background plus colored foreground path;
- Google Assistant: visible circle plus multiple colored path layers;
- Bitly: path ordering/background handling must not cover or invert the logo.

For every regression fixture, assert that the normalized SVG is nonblank and
unclipped, that required foreground colors survive, and that the manifest
background is the expected selected-source or neutral contrast color. Use small
synthetic fixtures in the repository; do not commit upstream artwork.

Regression fixtures are necessary but not sufficient. Add a full-catalog
visual-fidelity gate to every real build:

1. render the selected upstream SVG with a deterministic, sandboxed reference
   renderer;
2. render the normalized SVG using only the canonical format profile;
3. compose both at the format's 40x40 tile size with the 24x24 icon content area
   and the emitted `backgroundColor`;
4. compare visible alpha bounds, clipping, layer order, required foreground/fill
   colors, and the final background;
5. report source and normalized color palettes plus pixel-difference metrics for
   every canonical brand;
6. fail the build on every blank, clipped, covered, inverted, missing-color,
   wrong-background, wrong-foreground, or unsupported-paint result;
7. resolve each failure by correct normalization or deterministic alternate
   source selection; never suppress it or replace it with `#334155` merely to
   make the build pass.

Write the exhaustive result to a machine-readable report outside the generated
archive. The release gate is zero unresolved visual failures across the entire
catalog. Do not claim that a color is legally or officially authorized merely
because it occurs in an upstream file; retain source/guideline/license metadata
and the third-party-rights disclaimer.

The old 64 KiB SVG ceiling is forbidden. Match the application contract's 1 MiB
per-SVG denial-of-service budget, and do not introduce a smaller arbitrary byte,
brand, alias, or path-count limit. Security budgets remain mandatory and must be
streamed/bounded; they are not content-quality targets.

# Output format

The result must be ONE file, not a loose directory.

Use:

```text
otp-harbor-icons.otphicons
```

`.otphicons` should be a ZIP-based container with a custom file extension.

Do not expose `.zip` as the public pack extension.

The internal structure should contain at minimum:

```text
pack.json

icons/
  amazon.svg
  amazon-web-services.svg
  bosch.svg
  github.svg
  microsoft.svg
  siemens.svg
  ...

licenses/
  ...
```

Compatible importers should only need to understand the `.otphicons` format.

It should not need provider-specific Aegis/Simple Icons/Dashboard Icons parsing once the unified pack exists.

Keep `formatVersion: 1` unless an existing implemented schema already requires another version.

# Default output location

The normal user should not have to specify `--output`.

Resolve a platform-appropriate Downloads directory and propose:

```text
<Downloads>/otp-harbor-icons.otphicons
```

Examples conceptually:

```text
Windows:
C:\Users\<user>\Downloads\otp-harbor-icons.otphicons

Linux:
/home/<user>/Downloads/otp-harbor-icons.otphicons

macOS:
/Users/<user>/Downloads/otp-harbor-icons.otphicons
```

Do not hardcode these exact paths.

Use cross-platform directory resolution.

On Linux, respect XDG user-directory configuration where practical.

Fallback order:

1. platform/user Downloads directory;
2. user home directory;
3. current working directory.

# Interactive output selection

In normal interactive mode, show the proposed destination:

```text
Output:
C:\Users\User\Downloads\otp-harbor-icons.otphicons

Use this location? [Y/n]:
```

Pressing Enter must mean Yes.

If the user answers No, ask:

```text
Enter output directory or full output file path:
```

Accept either:

```text
D:\OTP Harbor\Icons
```

or:

```text
D:\OTP Harbor\Icons\custom-name.otphicons
```

If the supplied path is a directory, automatically append:

```text
otp-harbor-icons.otphicons
```

If the supplied path is a file path, require or normalize the `.otphicons` extension.

# Existing output behavior

If the destination file already exists, do not overwrite it silently in interactive mode.

Ask:

```text
The output file already exists.

Replace it? [Y/n]:
```

Pressing Enter must mean Yes.

Add:

```text
--force
```

for non-interactive/scripted overwrite behavior.

# Non-interactive mode

Support automation and CI.

For example:

```text
OtpHarbor.IconPackBuilder build \
  --output <path> \
  --non-interactive \
  --force
```

In `--non-interactive` mode:

- never prompt;
- use `--output` when provided;
- if no output is provided, either use the resolved default output path or fail only if that behavior is explicitly documented and tested;
- never overwrite an existing file unless `--force` is specified.

Keep the behavior deterministic and script-friendly.

# Interactive console experience

Use an ASCII-only in-place progress spinner and bar so the display remains
readable in legacy Windows consoles and Visual Studio external consoles. Show
percentage, completed/total brands, the current canonical brand, and elapsed
time. Do not emit dynamic progress when output is redirected or
`--non-interactive` is active.

End a successful build with a green `[+] Icon pack created successfully.`
status and a failed build with a red `[X]` status. Color is an enhancement:
the marker and text must remain unambiguous when color is unavailable or
disabled. Respect `NO_COLOR` and do not write ANSI control sequences to
redirected output.

When the Windows executable owns a newly opened console, including a direct
launch or Visual Studio external console, wait at `Press Enter to close...`
after success or failure. Do not pause when launched from an existing shell,
when input is redirected, or in `--non-interactive` mode.

After success, print a concise notice that third-party icons, names, and
trademarks remain subject to upstream licenses and rights holders, that the
pack includes its license/attribution/provenance records, and link to the
repository's legal documentation. Do not dump full license texts into the
console. The generated pack's manifest and `licenses/` entries are the
authoritative build-specific legal record.

Aim for a user experience approximately like:

```text
OTP Harbor Icon Pack Builder
────────────────────────────

The latest supported icon sources will be downloaded and merged:

  Aegis Icons
  Simple Icons
  Dashboard Icons

Output:
  C:\Users\User\Downloads\otp-harbor-icons.otphicons

Use this output location? [Y/n]:
>

Resolving upstream versions...

Aegis Icons       ✓ <version>
Simple Icons      ✓ <version>
Dashboard Icons   ✓ <commit>

Downloading...
Aegis Icons       ✓
Simple Icons      ✓
Dashboard Icons   ✓

Building catalog...

Source records:        ...
Canonical brands:      ...
Issuer aliases:        ...
Merged duplicates:     ...

Selected icons:
  Aegis:               ...
  Dashboard Icons:     ...
  Simple Icons:        ...

Validating pack...
✓ Valid

Created:
C:\Users\User\Downloads\otp-harbor-icons.otphicons
```

Do not depend on Unicode symbols if the output environment cannot display them; graceful ASCII fallback is acceptable.

# Atomic output generation

Never write directly into the final destination.

Use:

```text
temporary .otphicons
      |
      v
write full pack
      |
      v
reopen
      |
      v
validate pack/schema/assets
      |
      v
atomic move/replace
      |
      v
final .otphicons
```

If generation or validation fails:

- remove temporary output;
- do not damage an existing valid destination pack;
- report a useful error.

Add or preserve a regression test proving that an existing valid output remains untouched if generation/validation of the replacement fails.

# Security

All network downloads and archive inputs are untrusted.

Continue enforcing protections against:

- ZIP path traversal / Zip Slip,
- absolute paths,
- `..` traversal,
- excessive archive entry count,
- excessive compressed/uncompressed sizes,
- decompression bombs,
- duplicate conflicting paths,
- malformed JSON,
- unexpectedly large SVG files,
- unsupported source structures,
- incomplete/corrupted downloads.

Do not execute content from upstream archives.

Do not render SVGs.

SVGs are data.

Network downloads should use appropriate:

- timeouts,
- cancellation tokens,
- HTTP status validation,
- redirect handling,
- maximum-size limits.

Do not cache invalid/incomplete downloads as valid source versions.

# Diagnostics

Errors should identify:

- provider,
- resolved version/revision,
- source URL or local source path when appropriate,
- upstream record/icon where known,
- failure reason.

Keep machine-readable conflict reporting.

The successful build summary should include at least:

- resolved upstream versions,
- records read per provider,
- canonical brand count,
- issuer alias count,
- merged record count,
- conflict count,
- skipped/invalid record count,
- selected icon count per provider,
- final output path.

Order provider summaries deterministically.

# CLI provider registration

Provider identifiers should have one source of truth.

Build provider lookup from each `IIconSourceProvider.Id`.

Do not duplicate IDs independently inside `CliApplication`.

Provider IDs at the CLI boundary should be case-insensitive.

Validate provider preference values:

- reject unknown providers;
- reject duplicate provider IDs.

# Expected exception handling

Expected build failures must return clean CLI errors rather than unhandled stack traces.

Handle at least:

- input validation,
- invalid provider data,
- malformed JSON,
- invalid formats,
- I/O errors,
- access errors,
- download/network failures,
- build conflicts,
- cancellation.

Handle cancellation explicitly and return an appropriate non-zero cancellation exit code.

# Stale conflict reports

If a previous build created a conflict report but the current build succeeds completely, ensure the old conflict report does not misleadingly remain associated with the new successful build.

Clean up or update stale conflict-report behavior appropriately.

# Testing requirements

Update/add tests for the new workflow.

Cover at least:

- no-argument invocation starts build instead of help;
- `build` with no provider inputs uses automatic acquisition;
- output defaults to the platform Downloads location;
- Downloads fallback behavior;
- interactive acceptance of default path;
- interactive custom directory;
- interactive custom file path;
- existing output prompts and Enter confirms replacement;
- `--force`;
- `--non-interactive`;
- `--offline`;
- cached source reuse;
- explicit local provider override;
- local override taking precedence over network acquisition;
- exact upstream version/commit pinning;
- prevention of mixed Dashboard Icons revisions;
- corrupted cache rejection;
- network-download failure handling;
- cancellation;
- Aegis parser;
- Simple Icons parser;
- Dashboard Icons parser;
- issuer normalization;
- canonical mapping;
- alias deduplication;
- alias collision failure;
- Amazon vs AWS separation;
- Microsoft vs Microsoft 365 separation;
- source override selection;
- selected-source background color behavior;
- `#334155` fallback;
- deterministic output ordering;
- Zip Slip rejection;
- archive size/count limits;
- pack read/write validation;
- atomic output replacement;
- existing valid pack remains unchanged when replacement build fails.

Tests must not depend on the live internet.

Abstract upstream acquisition sufficiently that tests can use fake/local HTTP responses or provider-source fixtures.

Use small synthetic fixtures rather than committing full third-party icon packs.

# README

Update the README to describe the new user experience.

The main usage should now be:

```text
OtpHarbor.IconPackBuilder
```

and:

```text
OtpHarbor.IconPackBuilder build
```

Explain that the builder automatically downloads the latest supported icon sources and produces:

```text
otp-harbor-icons.otphicons
```

Document:

- automatic upstream acquisition,
- default output location,
- custom output selection,
- cache behavior,
- `--offline`,
- local provider overrides,
- `--refresh`,
- `--non-interactive`,
- `--force`,
- provenance/version recording,
- the fact that generated packs are consumed offline/local-first.

Also explain that third-party trademarks/icons remain owned by their respective rights holders.

The repository must not publish or commit generated combined third-party icon packs.

# Repository hygiene

Ensure `.gitignore` excludes:

- downloaded upstream archives,
- extracted upstream icon trees,
- cache directories if local cache can reside inside the repo during development,
- generated `.otphicons`,
- temporary files,
- normal .NET build output.

Do not commit any complete third-party icon collection.

# Completion requirements

Before reporting completion:

1. build the full solution;
2. run all tests;
3. run a complete end-to-end build using controlled/local test sources;
4. verify the produced `.otphicons` can be reopened and validated;
5. verify canonical background colors match the documented format;
6. verify alias conflicts still fail deterministically;
7. verify the final output is one `.otphicons` file;
8. verify no third-party source collection or generated combined pack is tracked by Git;
9. summarize:
    - architecture changes,
    - acquisition strategy per provider,
    - cache locations/behavior,
    - output-path behavior,
    - pack-format changes,
    - compatibility assumptions,
    - any remaining limitations.

Do not stop after scaffolding.

Implement the complete workflow.
