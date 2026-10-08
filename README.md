# OTP Harbor Icon Pack Builder

OTP Harbor Icon Pack Builder creates one local, unified `.otphicons` pack from the latest supported Aegis Icons, Simple Icons, and Dashboard Icons sources. The builder performs upstream network access; OTP Harbor imports the finished file and remains offline/local-first.

## Legal and distribution model

This repository does not redistribute upstream icon collections or generated packs. Downloaded archives, cache data, extracted trees, generated `.otphicons` files, conflict reports, and visual-verification reports are excluded from Git.

Icons, names, and trademarks remain the property of their respective rights holders. The generated pack records upstream versions, source URLs, archive hashes, per-icon provenance, and available license or attribution files. Review upstream licenses, trademark rules, and brand guidelines before using or sharing a pack.

Dashboard Icons identifies its repository license as [Apache License 2.0](https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE). The builder copies that license from the same immutable commit as the selected Dashboard Icons metadata and assets, records `Apache-2.0` plus a revision-pinned license URL in source metadata, and preserves the notice in the generated pack. Apache-2.0 does not grant permission to use third-party trademarks represented by individual icons; applicable trademark and brand-usage rules remain separate.

## Requirements

- .NET 8 SDK or newer
- Internet access for the normal build workflow

No Node.js, Python, or provider-specific build tools are required.

## Build and run

```powershell
dotnet build .\OtpHarbor.IconPackBuilder.sln --configuration Release
dotnet run --project .\src\OtpHarbor.IconPackBuilder
```

Both of these start the normal build:

```text
OtpHarbor.IconPackBuilder
OtpHarbor.IconPackBuilder build
```

The builder resolves immutable upstream versions/revisions, downloads or reuses validated cache entries, merges all three catalogs, validates the completed pack, and proposes this output:

```text
<your Downloads directory>/otp-harbor-icons.otphicons
```

In an interactive terminal, press Enter to accept the proposed location. If you decline it, enter either a directory or a full file path. A directory receives the default `otp-harbor-icons.otphicons` filename; another file extension is normalized to `.otphicons`.

While the canonical catalog is being normalized and visually verified, interactive runs show an in-place ASCII progress display with a spinner, bar, percentage, completed/total brand count, current canonical brand, and elapsed time. The ASCII glyphs remain readable in legacy Windows and Visual Studio consoles. Redirected and `--non-interactive` runs omit dynamic progress so automation logs remain stable.

Successful interactive builds end with a green `[+] Icon pack created successfully.` status; failures use a red `[X]` status. The markers remain readable when the console does not support color. When the executable owns a newly opened Windows console, such as a direct launch or a Visual Studio external console, it waits at `Press Enter to close...` after either outcome. Runs from an existing PowerShell, Command Prompt, or Windows Terminal session, redirected runs, and `--non-interactive` runs do not pause.

The success summary displays a concise third-party-rights notice and links to this repository's legal and distribution section. The generated pack's manifest and `licenses/` entries remain the authoritative record of the exact sources, licenses, attribution, and provenance included in that build.

An existing file is never overwritten without an interactive confirmation. The replacement prompt is `[Y/n]`, so pressing Enter confirms replacement; answer `n` to preserve the existing file.

## Automation

```powershell
dotnet run --project .\src\OtpHarbor.IconPackBuilder -- build `
  --output "C:\Packs\otp-harbor-icons.otphicons" `
  --non-interactive `
  --force
```

`--non-interactive` never prompts. Without `--output`, it uses the platform default Downloads path. It refuses to replace an existing file unless `--force` is supplied.

Options:

```text
--output <directory-or-file>
--offline
--refresh
--non-interactive
--force
--cache <directory>
--mappings <directory>
--provider-preference <comma-separated provider IDs>
--conflict-report <path.json>
--aegis <local.zip>
--simple-icons <local.zip>
--dashboard-icons <local.zip>
```

Provider IDs and provider-preference values are case-insensitive. An explicit source override wins first. Otherwise, the builder normalizes and visually checks every contributing candidate, prefers faithfully preserved full-color artwork over a monochrome badge, then considers source-backed backgrounds and applies the configured Aegis/Dashboard/Simple Icons preference as a deterministic tie-breaker between visually equivalent candidates.

## Acquisition and cache behavior

The normal build acquires all three providers:

- Aegis Icons: the `aegis-icons.zip` asset from the latest supported GitHub release.
- Simple Icons: a source archive pinned to the latest published GitHub release tag.
- Dashboard Icons: only required metadata, SVG files, and license data, all pinned to one exact `main` commit SHA.

Dashboard SVGs that exceed OTP Harbor's 1 MiB security budget, are absent at the pinned revision, or fail SVG safety validation are excluded individually rather than aborting the entire build. Safe, resolved local fragment references such as `href="#shape"` are accepted as upstream input and expanded during normalization; external references, unresolved fragments, duplicate IDs, scripts, `foreignObject`, and DTDs are rejected. Downloaded rejected SVGs and a Markdown table containing source path, byte size, cached diagnostic path, and reason are stored in that revision's cache entry under `skipped-svgs/`. The CLI prints the report path. These diagnostics are not included in the final `.otphicons` pack.

Cache data is stored by provider and immutable version/revision:

- Windows: `%LOCALAPPDATA%\OTP Harbor\IconPackBuilder\cache`
- Linux: `$XDG_CACHE_HOME/otp-harbor/icon-pack-builder`, or `~/.cache/otp-harbor/icon-pack-builder`
- macOS: `~/Library/Caches/OTP Harbor/IconPackBuilder`

Every cache entry contains a SHA-256-protected source archive and manifest. Incomplete, malformed, unsafe, or hash-mismatched entries are not reused. `--refresh` redownloads the currently resolved sources.

`--offline` performs no network access. Every provider must then be available from a validated cache entry or an explicit local override. A local provider option always overrides automatic acquisition for that provider; unspecified providers are still acquired normally unless `--offline` is active.

## Canonical brands and issuer matching

OTP Harbor resolves icons only from the mandatory TOTP `issuer`. The optional account name is intentionally excluded because it often contains an email address, username, ID, or customer number.

The builder creates stable, provider-independent, lowercase kebab-case canonical IDs. Provider slugs are source identities, not permanent OTP Harbor identities. Resolution follows this order:

1. explicit canonical mappings;
2. conservative exact normalized-name matching;
3. a distinct deterministic canonical brand when no safe merge exists.

There is no fuzzy matching. Amazon remains distinct from Amazon Web Services, Microsoft from Microsoft 365, and GitHub from GitHub Actions unless a mapping explicitly says otherwise.

Aliases from all matching providers and source-controlled mappings are merged with explicit confidence. Manual aliases are authoritative, exact brand names and source identities come next, and descriptive provider aliases are lowest confidence. A lower-confidence alias cannot override an exact identity. Descriptive aliases claimed by multiple brands are suppressed and counted because no safe mapping exists. Equal-confidence manual or identity collisions still fail and produce a deterministic machine-readable conflict report instead of choosing an icon arbitrarily.

Product decisions live in:

- `mappings/canonical-brands.json`
- `mappings/issuer-aliases.json`
- `mappings/source-overrides.json`

## Background colors

Every output brand has a required `backgroundColor`. The deterministic precedence is:

1. explicit canonical mapping override;
2. native color metadata from the selected provider;
3. a safe full-logo circle or rectangle from the selected SVG when promoting it preserves the rendered tile;
4. a contrast-checked neutral fallback: `#FFFFFF` for dark/colored foregrounds when safe, otherwise `#334155`.

Color never leaks from an unselected provider record. Inline styles, inherited paint, and provider CSS are resolved before this decision, so an embedded brand surface such as Bitdefender's red circle is not lost.

## SVG normalization and the visual release gate

Upstream SVGs are normalized before packaging. The output profile contains a finite positive `viewBox` and path geometry only; local `<use>` references and basic shapes are expanded, CSS and paint inheritance are resolved, and unsupported gradients, filters, masks, clipping, strokes, opacity, or similar browser features are flattened to safe solid path layers. If structural normalization is not visually faithful, the builder deterministically converts a sandboxed reference rendering into path data instead of shipping unsupported SVG behavior. It tries 96 pixels first for sharp application scaling, then 48 pixels, and uses an exact 24-pixel fallback only when a higher-resolution representation cannot satisfy the same 24-pixel visual-equivalence gate. A true canonical vector candidate always outranks a raster-path fallback when both are available.

Every selected source is then compared with its normalized result on the contract-defined 40×40 tile with a centered 24×24 content area. The build fails if any brand remains blank, clipped, incorrectly painted, or materially different. A successful build writes a deterministic sibling report:

```text
otp-harbor-icons.otphicons
otp-harbor-icons.visual-report.json
```

The JSON report contains the selected upstream asset, background, normalization operations, source and normalized palettes, pixel metrics, and `unresolvedFailures`. A releasable pack always has `unresolvedFailures: 0`. The report is diagnostic only and is not imported by OTP Harbor.

## Pack format

`.otphicons` is a ZIP container with a custom public extension:

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

`pack.json` uses `formatVersion: 1` and camel-case JSON properties. It contains pinned upstream provenance, archive SHA-256 values, canonical brands, selected source metadata, all contributing source references, original issuer aliases, a normalized issuer lookup index, and license references. See [`schemas/otp-harbor-icon-pack.schema.json`](schemas/otp-harbor-icon-pack.schema.json) and [`docs/OTP-HARBOR-INTEGRATION.md`](docs/OTP-HARBOR-INTEGRATION.md).

The builder writes to a temporary file in the destination directory, reopens and validates the completed archive, writes the zero-failure visual report, and only then publishes it. A failed pack replacement leaves an existing destination untouched. A successful build removes a stale associated conflict report.

## Input security

All network responses, archives, metadata, and SVGs are untrusted. The builder enforces HTTPS, bounded redirects and downloads, timeouts, cancellation, ZIP path safety, duplicate-path rejection, entry count and size limits, decompression-ratio limits, bounded JSON depth, expected provider structures, SVG size and XML validation, and rejection of external or executable SVG content. It never executes upstream content or permits external renderer resources. A local sandboxed vector renderer is used only for deterministic pixel comparison and safe path flattening during the user-run build; OTP Harbor itself does not render untrusted provider SVG features or access the network.

## Development

```powershell
dotnet test .\OtpHarbor.IconPackBuilder.sln --configuration Release
```

Tests create small synthetic archives at runtime and do not use the live internet. Generated packs and complete third-party icon collections must never be committed or published from this repository.
