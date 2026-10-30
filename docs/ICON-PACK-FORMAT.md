# `.otphicons` icon pack format

This document describes the provider-neutral archive produced by OTP Harbor Icon Pack Builder. It is intended for pack inspection, validation, and compatible importer implementations.

## Container

An `.otphicons` file is a ZIP archive. Format 1 has this frozen layout:

```text
pack.json
icons/
  <canonical-brand-id>.svg
licenses/
  aegis/
  dashboard-icons/
  simple-icons/
```

Format 2 is PNG-only:

```text
pack.json
runtime/
  foreground-128/
    <canonical-brand-id>.png
licenses/
  ...
```

Format 3 is also PNG-only and stores content-addressed theme appearances:

```text
pack.json
runtime/
  appearance-128/
    <lowercase-png-sha256>.png
licenses/
  ...
```

There is exactly one root `pack.json`. Every icon and license reference is a container-relative path. Importers must reject absolute paths, traversal segments, backslashes, duplicate normalized paths, unreferenced files, and entries outside the documented roots.

## Manifest

`pack.json` is UTF-8 JSON using camel-case property names. Format 1 has this high-level structure:

```json
{
  "formatVersion": 1,
  "packId": "otp-harbor-icons",
  "name": "OTP Harbor Icons",
  "sources": [],
  "brands": [],
  "issuerAliases": []
}
```

The frozen format-1 machine-readable definition is
[`../schemas/otp-harbor-icon-pack.schema.json`](../schemas/otp-harbor-icon-pack.schema.json).
The separate format-2 definition is
[`../schemas/otp-harbor-icon-pack-v2.schema.json`](../schemas/otp-harbor-icon-pack-v2.schema.json).
The separate format-3 definition is
[`../schemas/otp-harbor-icon-pack-v3.schema.json`](../schemas/otp-harbor-icon-pack-v3.schema.json).

### Sources

Each `sources` entry records one exact upstream input:

```json
{
  "provider": "dashboard-icons",
  "inputFileName": "dashboard-icons-<revision>.zip",
  "sha256": "<64 lowercase hexadecimal characters>",
  "version": null,
  "revision": "<full commit SHA>",
  "sourceUrl": "<immutable upstream URL>",
  "metadata": {
    "licenseType": "Apache-2.0",
    "licenseUrl": "<revision-pinned license URL>"
  },
  "licenseFiles": [
    "licenses/dashboard-icons/license"
  ]
}
```

`version` or `revision` identifies the immutable upstream snapshot. `sha256` covers the normalized source archive consumed by the builder. Referenced license files are included in the ZIP and must be preserved by compatible importers.

### Brands

Each format-1 `brands` entry is already canonicalized and has exactly one
selected SVG:

```json
{
  "id": "amazon-web-services",
  "displayName": "Amazon Web Services",
  "backgroundColor": "#FFFFFF",
  "icon": "icons/amazon-web-services.svg",
  "issuerAliases": [
    "Amazon AWS",
    "Amazon Web Services",
    "AWS"
  ],
  "selectedSource": {
    "provider": "simple-icons",
    "sourceId": "amazonwebservices",
    "metadata": {}
  },
  "sources": []
}
```

- `id` is a stable lowercase kebab-case identifier.
- `displayName` is user-facing text.
- `backgroundColor` is an uppercase six-digit CSS color.
- `icon` points to the normalized SVG selected by the builder.
- `issuerAliases` retains original alias strings for diagnostics and display.
- `selectedSource` records the winning upstream asset.
- `sources` records all contributing provider identities and provenance.

Importers must use the supplied canonical ID and selected icon. They must not repeat provider selection, infer a different background, or recanonicalize the record from a provider slug.

### Format 2 PNG-only brand

Format 2 removes the format-1 `icon` member and requires `runtimeIcon` on every
brand. A complete v2 brand is conceptually:

```json
{
  "id": "amazon-web-services",
  "displayName": "Amazon Web Services",
  "backgroundColor": "#FFFFFF",
  "issuerAliases": ["Amazon AWS", "Amazon Web Services", "AWS"],
  "selectedSource": {
    "provider": "simple-icons",
    "sourceId": "amazonwebservices",
    "metadata": {}
  },
  "sources": [],
  "runtimeIcon": {
    "path": "runtime/foreground-128/amazon-web-services.png",
    "mediaType": "image/png",
    "width": 128,
    "height": 128,
    "sha256": "<64 lowercase hexadecimal characters>",
    "renderProfile": "otp-harbor-foreground-v1"
  }
}
```

The path is exactly `runtime/foreground-128/<canonical-brand-id>.png`. It is
provider-independent. `sha256` covers the exact packaged PNG bytes and is the
runtime asset revision/cache key. A v2 archive is invalid unless every brand
has exactly one referenced runtime PNG and every PNG has exactly one brand.
The `icon` member is forbidden even when null, and no SVG may occur in the v2
archive. A v1 archive must contain neither `runtimeIcon` nor `runtime/` files.

### Format 3 themed brand

Format 3 removes the legacy per-brand `icon`, `backgroundColor`, and
`runtimeIcon` members. Every brand contains two fully resolved appearances:

```json
{
  "id": "example",
  "displayName": "Example",
  "issuerAliases": ["Example"],
  "selectedSource": { "provider": "dashboard-icons", "sourceId": "example", "metadata": {} },
  "sources": [],
  "appearances": {
    "lightTheme": {
      "asset": {
        "path": "runtime/appearance-128/<sha256>.png",
        "mediaType": "image/png",
        "width": 128,
        "height": 128,
        "sha256": "<sha256>",
        "renderProfile": "otp-harbor-themed-appearance-v1"
      },
      "assetRole": "foreground",
      "background": { "mode": "solid", "color": "#FFFFFF" },
      "selection": "native-theme-variant",
      "source": { "provider": "dashboard-icons", "sourceId": "example-dark", "assetPath": "svg/example-dark.svg" },
      "appearanceSelectionReason": "Selected provider-supplied lightTheme artwork after normalization and visual verification."
    },
    "darkTheme": {}
  }
}
```

The abbreviated `darkTheme` above has the same required shape as
`lightTheme`; the schema does not permit an empty appearance.

`assetRole` is normative. `foreground` means the PNG is transparent logo
artwork and requires a `solid` tile background. `complete-icon` means the PNG
already contains its intended composition and requires `background.mode` of
`embedded` with no color. Consumers must not draw a second branded background
behind a complete icon.

The asset path is exactly
`runtime/appearance-128/<lowercase-sha256>.png`, and the filename hash must
equal both the descriptor SHA-256 and the packaged bytes. Light and dark
appearances may reference the same path. Sharing is required when the rendered
bytes are identical and is allowed across brands. Every PNG must be referenced;
there is no one-file-per-brand rule in v3.

`selection`, `source`, and `appearanceSelectionReason` make the builder's
theme-specific decision auditable. Provider conventions are resolved by the
builder and must not be reinterpreted by consumers.

### Issuer alias index

`issuerAliases` is the deterministic lookup index:

```json
{
  "key": "amazon web services",
  "brandId": "amazon-web-services"
}
```

Each normalized key maps to exactly one existing brand. Ambiguous aliases are intentionally absent. The optional account name is never an input to automatic brand matching.

Normalization performs Unicode compatibility normalization, invariant case folding, safe separator normalization, whitespace collapse, and trimming. Compatible importers should use the same behavior as `IssuerNormalizer` in this repository and must not add fuzzy matching.

## Normalized SVG profile

Every format-1 packaged SVG and every format-2 or format-3 internal canonical reference:

- is well-formed XML with one `svg` root;
- has a finite, positive `viewBox`;
- uses path geometry supported by the format;
- contains no scripts, event handlers, external references, DTDs, or `foreignObject`;
- contains no unresolved `<use>`, CSS, gradients, patterns, masks, filters, clipping, strokes, transforms, or opacity behavior;
- remains within the per-entry size limit;
- has passed the builder's source-versus-output visual comparison.

Format-1 importers must treat every SVG as untrusted data and apply bounded
parsing and path validation. Format-2 and format-3 consumers never receive or parse SVG;
the builder discards its internal canonical references before packaging.

## Runtime foreground profile `otp-harbor-foreground-v1`

This section is normative for format 2.

- The stored image is exactly 128 by 128 pixels, 8-bit RGBA, non-interlaced,
  lossless PNG (`image/png`).
- The canvas begins fully transparent. It contains foreground artwork only;
  `backgroundColor` is not baked into the PNG and is drawn separately by the
  consumer.
- The builder-internal canonical SVG is statically validated and normalized before rendering.
  Only the normalized path-only SVG profile is passed to the bounded,
  in-process renderer. No scripts, animation, external references, remote
  resources, fonts, shell commands, or external programs are allowed.
- Canonical visible geometry is uniformly scaled to fit the 128 by 128 viewport,
  centered on both axes, clipped to the viewport, and never stretched. Layer
  order and alpha compositing follow canonical SVG document order.
- A canonical path with no `fill` uses solid white (`#FFFFFF`) for this runtime
  profile. Explicit solid fills, including multicolor fills, are preserved.
- The output must contain at least one nontransparent pixel. Blank output,
  invalid bounds, failed decode, wrong dimensions, or visual mismatch rejects
  the candidate.
- The PNG contains exactly one `IHDR`, one or more contiguous `IDAT` chunks,
  and exactly one final `IEND`, with no trailing bytes. Text,
  timestamps, EXIF, ICC/color profiles, and all other ancillary chunks are
  forbidden. The builder uses a fixed filter (None), a deterministic zlib
  stream containing fixed-Huffman DEFLATE blocks and scanline filter type 0,
  deterministic greedy match
  selection, and fixed chunk ordering; equal RGBA pixels therefore produce
  equal PNG bytes. Dynamic-Huffman output and encoder-specific metadata are not
  used.
- Format-2 ZIP file entries use the stored/no-compression method so the outer
  archive does not reintroduce platform-dependent compressor output. Entry
  ordering, external attributes, and the DOS timestamp (`1980-01-01T00:00:00Z`)
  are fixed. Format-1 compression and layout remain frozen.
- Encoded content is limited to 1 MiB. Decoded allocation is exactly
  `128 * 128 * 4` bytes. The builder reopens the encoded PNG, validates its
  signature, chunks, CRCs, profile, dimensions, decoded budget, and SHA-256,
  then compares the exact packaged bytes with the internal canonical SVG
  reference before publication.

The verification comparison draws `backgroundColor` into a 40 by 40 tile and
fits the foreground into its centered 24 by 24 content square. It measures the
pixel difference and palettes after composition. Source-to-canonical SVG
verification remains a mandatory builder-internal earlier gate;
internal-canonical-to-packaged-PNG verification is an additional v2 gate.
The builder reopens the completed archive and repeats structural, hash,
coverage, blank/covered, and visual-reference validation before atomic
publication. Consumers must nevertheless treat an imported archive as
untrusted and perform their own bounded validation. After one successful full
PNG decode during staged import, installation may stream the validated bytes
without a second decode while recomputing and checking SHA-256.

## Themed appearance profile `otp-harbor-themed-appearance-v1`

This section is normative for format 3. It inherits the format-2 PNG encoding,
dimension, fitting, safety, bounded-decoding, deterministic-ZIP, blank-output,
and visual-comparison rules. Its additional rules are:

- the builder resolves both `lightTheme` and `darkTheme` before packaging;
- visible geometry is uniformly fitted into the centered 112 by 112 safe
  content area from pixel 8 through pixel 119 on each axis. The surrounding
  eight-pixel transparent inset prevents wide marks and wordmarks from touching
  the tile edge; at a 24-logical-pixel presentation it represents a 1.5-pixel
  margin. Format-1 and format-2 fitting remain frozen and unchanged;
- original multicolor paint and layer order are preserved; multicolor artwork
  is never automatically inverted or recolored for a theme;
- a Simple Icons primary brand color is foreground paint, not an inferred tile
  background;
- when that materialized monochrome brand mark has at least 1.25:1 contrast for
  at least 90 percent of its nearly opaque pixels against the application theme
  surface, it is a transparent `complete-icon` with `embedded` background mode;
  otherwise it remains `foreground` artwork with a deterministic contrasting
  solid tile. This rule changes presentation only and never recolors the mark;
- a provider-supplied Dashboard light/dark asset may be selected only for its
  intended appearance and only after the same normalization and visual gates
  as a default asset;
- recognized Aegis variations remain associated with their primary record for
  provenance, but are not selected automatically without an explicit reviewed
  mapping because they do not encode an authoritative theme contract;
- Dashboard's dark artwork variant is evaluated for `lightTheme`, and its light
  artwork variant is evaluated for `darkTheme`; these names describe the
  artwork, not the application theme;
- source artwork with a recognized embedded composition is retained as a
  `complete-icon`; otherwise the result is a transparent `foreground` with a
  deterministic contrast-checked solid background. Recognition requires a
  large solid source-painted backplate that geometrically contains a later,
  contrasting detail layer; adjacent multicolor marks and transparent holes
  alone do not qualify. The source paint and geometry are preserved, so this
  classification never invents a generic white tile;
- a reviewed badge composition may convert only an explicitly mapped
  provider/source record into a `complete-icon`. The deterministic operation
  applies its declared matte color only to fully enclosed transparent regions,
  retains boundary-connected transparency and antialiased exterior edges, and
  records `reviewed-badge-composition` as the selection reason. It is never
  inferred merely because paths contain enclosed transparent regions;
- an unpaired/default foreground that still needs a tile uses a neutral white
  tile in both application themes. A dark tile is used only with
  provider-supplied artwork explicitly intended for the dark application theme.
  This preserves source paint instead of automatically recoloring it;
- a reviewed `source-overrides.json` entry may declare light/dark foreground
  colors for one exact canonical brand and provider/source record when the
  source supports those monochrome treatments. The entry requires a rationale,
  preserves transparent exterior pixels, emits the existing
  `brand-color-foreground` selection, and cannot affect v1/v2. This is an
  explicit review boundary, not a general inversion or recoloring heuristic;
- foreground/background paint collisions below the 1.1:1 visibility boundary
  are rejected or resolved by selecting a deterministic neutral background;
- the two appearances are rendered independently, then identical PNG bytes are
  deduplicated by lowercase SHA-256;
- after static source safety validation and canonical structural normalization,
  format 3 materializes the selected appearance's inherited paint and renders
  that validated source exactly once at the final 128-pixel/inset geometry. The
  exact bounded RGBA buffer is deterministically encoded, reopened, and compared
  byte-for-pixel with the packaged PNG. The normalized SVG remains the bounded
  internal geometry/color/background audit artifact and is never packaged. This
  avoids both repeated catalog rendering and any 24-to-128 raster upscale;
- each appearance is compared on a 40 by 40 preview tile with a centered 24 by
  24 content square. Complete icons use the theme preview surface only for
  verification; it is not added to the PNG.

The 128-pixel raster remains the single v3 asset size because it covers the
current 24–26 logical-pixel UI use at modern Android densities while keeping
the pack and bitmap cache bounded. A later larger-size profile requires a new
profile identifier and measurements; consumers must not upscale the contract
silently.

The source-controlled `mappings/appearance-compositions.json` file is the
review boundary for composed badges. Each entry identifies the canonical
brand, exact provider/source record, supported composition mode, matte color,
and review rationale. See the
[Simple Icons badge-composition audit](SIMPLE-ICONS-BADGE-COMPOSITION-AUDIT.md).

## Visual report

A successful build also writes a sibling diagnostic file:

```text
otp-harbor-icons.otphicons
otp-harbor-icons.visual-report.json
```

The visual report contains per-brand palettes, operations, and pixel-comparison
metrics. For v1 it reports source-to-packaged-canonical-SVG verification. For
v2 it records the operation from the selected source through the internal
canonical reference to the final packaged PNG. For v3 it contains exactly two
appearance entries per brand, including role and selection reason. No packaged
SVG is implied for either PNG-only format.
Its `formatVersion` equals the format version of the pack produced by the same
transaction.
It is not an archive member or import input. A successful catalog has
`unresolvedFailures: 0`.

## Rights evidence report

A successful build also writes otp-harbor-icons.rights-report.json. It records
the selected provider/source ID, narrowly defined evidence status, license type
and evidence URL where available, and the active rights policy. It contains
notLegalClearance: true because automated classification cannot determine
trademark use, endorsement, copyright exceptions, jurisdiction, or compliance
for a concrete use.
Its `formatVersion` likewise equals the associated pack format version.

The same status, basis, and evidence fields are copied into selected-source and
source-reference metadata. This is additive format-1 metadata and does not
change provider-independent brand resolution.

## Validation requirements

A compatible importer should reject the complete archive without changing previously installed data when any of these conditions occurs:

- `formatVersion` is unsupported;
- required manifest fields are missing or malformed;
- archive entry counts, compressed size, expanded size, per-entry size, or compression ratios exceed local limits;
- a ZIP path is absolute, traversing, duplicated, conflicting, or unreferenced;
- a required SVG, runtime PNG, or license path is missing or outside its allowed root;
- canonical IDs, colors, hashes, or aliases violate the schema;
- an alias references a nonexistent brand or maps ambiguously;
- selected-source provenance is inconsistent;
- a format-1 SVG violates the normalized profile;
- a v1 manifest or archive contains a runtime member;
- a v2 manifest contains `icon`, a v2 archive contains any SVG, or a v2 brand
  lacks its runtime asset;
- a v2 brand uses a noncanonical runtime path/profile/media type/dimension, has
  an invalid/blank/covered PNG, or declares the wrong runtime SHA-256;
- v2 brand and runtime-PNG coverage is not one-to-one;
- a v3 manifest contains a legacy `icon`, top-level `backgroundColor`, or
  `runtimeIcon`, or its archive contains an SVG;
- a v3 brand lacks either theme appearance, has inconsistent
  role/background semantics, has invalid appearance provenance, or references
  a non-content-addressed, missing, structurally invalid, or hash-mismatched
  PNG;
- a v3 archive contains an unreferenced asset. Multiple v3 appearance
  descriptors may intentionally reference one identical content-addressed PNG.

Validation and installation should be transactional: fully stage and validate the new pack before replacing any prior pack.

Blank/covered output and the other semantic pixel checks are mandatory builder
publication gates, not catalog-wide consumer import work. A consumer validates
all hashes and bounded PNG container structure transactionally, then decodes
assets lazily outside the UI thread. It need not inflate the entire catalog at
import merely to repeat the builder's visual analysis.

## Licensing and provenance

The archive preserves source revisions, hashes, metadata, selected-source
provenance, and recognized license, credit, disclaimer, README, attribution,
or trademark files available in the pinned source snapshots. Preservation does
not guarantee that upstream supplied every notice legally required for an
individual asset.

Repository licenses do not necessarily grant trademark rights for the brands depicted by individual icons. Consumers remain responsible for applicable licenses, attribution requirements, trademark policies, and brand guidelines.

Runtime PNGs are deterministic derivatives created by the user-side builder
from the selected source through an internal canonical SVG. Rasterization grants no additional copyright,
trademark, endorsement, or redistribution right. A runtime asset inherits the
same selected-source identity, provenance, evidence, notices, and limitations
as its selected source; it is not a separately sourced work.

## Format evolution

Formats 1 and 2 are frozen and remain explicitly selectable with
`--format-version 1` and `--format-version 2`. Format 3 is the builder default
and may also be selected explicitly with `--format-version 3`. Importers must
dispatch on `formatVersion` and reject unknown versions rather than partially
interpreting them. No reader may ignore unknown manifest members or
unreferenced files. See the
[format v3 consumer handoff](FORMAT-V3-CONSUMER-HANDOFF.md) before coordinating
a consumer release.
