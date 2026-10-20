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

Format 2 adds one runtime foreground for every canonical SVG:

```text
pack.json
icons/
  <canonical-brand-id>.svg
runtime/
  foreground-128/
    <canonical-brand-id>.png
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

Each `brands` entry is already canonicalized and has exactly one selected icon:

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

### Format 2 runtime icon

Format 2 keeps every format-1 canonical SVG and adds this required member to
every brand:

```json
{
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
has exactly one referenced canonical SVG and one referenced runtime PNG. A v1
archive must contain neither `runtimeIcon` nor `runtime/` files.

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

Every packaged icon:

- is well-formed XML with one `svg` root;
- has a finite, positive `viewBox`;
- uses path geometry supported by the format;
- contains no scripts, event handlers, external references, DTDs, or `foreignObject`;
- contains no unresolved `<use>`, CSS, gradients, patterns, masks, filters, clipping, strokes, transforms, or opacity behavior;
- remains within the per-entry size limit;
- has passed the builder's source-versus-output visual comparison.

Importers should still treat every archive and SVG as untrusted data and apply their own bounded parsing and path validation.

## Runtime foreground profile `otp-harbor-foreground-v1`

This section is normative for format 2.

- The stored image is exactly 128 by 128 pixels, 8-bit RGBA, non-interlaced,
  lossless PNG (`image/png`).
- The canvas begins fully transparent. It contains foreground artwork only;
  `backgroundColor` is not baked into the PNG and is drawn separately by the
  consumer.
- The canonical SVG is statically validated and normalized before rendering.
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
- The PNG contains only `IHDR`, contiguous `IDAT`, and `IEND` chunks. Text,
  timestamps, EXIF, ICC/color profiles, and all other ancillary chunks are
  forbidden. The builder uses a fixed filter (None), a deterministic zlib
  stream containing fixed-Huffman DEFLATE blocks, deterministic greedy match
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
  then compares it with the canonical SVG before packaging.

The verification comparison draws `backgroundColor` into a 40 by 40 tile and
fits the foreground into its centered 24 by 24 content square. It measures the
pixel difference and palettes after composition. Source-to-canonical SVG
verification remains mandatory as an earlier gate; canonical-to-runtime PNG
verification is an additional v2 gate.

## Visual report

A successful build also writes a sibling diagnostic file:

```text
otp-harbor-icons.otphicons
otp-harbor-icons.visual-report.json
```

The visual report contains per-brand palettes, operations, and pixel-comparison
metrics. For v1 it reports source-to-canonical SVG verification. For v2 it also
records the runtime rendering operation and canonical-to-packaged-PNG metrics.
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
- an icon or license path is missing or outside its allowed root;
- canonical IDs, colors, hashes, or aliases violate the schema;
- an alias references a nonexistent brand or maps ambiguously;
- selected-source provenance is inconsistent;
- an SVG violates the normalized profile.
- a v1 manifest or archive contains a runtime member;
- a v2 brand lacks either asset, uses a noncanonical runtime path/profile/media
  type/dimension, has an invalid PNG, or declares the wrong runtime SHA-256;
- canonical SVG and runtime PNG coverage is not one-to-one or their visual
  comparison is unresolved.

Validation and installation should be transactional: fully stage and validate the new pack before replacing any prior pack.

## Licensing and provenance

The archive preserves source revisions, hashes, metadata, selected-source
provenance, and recognized license, credit, disclaimer, README, attribution,
or trademark files available in the pinned source snapshots. Preservation does
not guarantee that upstream supplied every notice legally required for an
individual asset.

Repository licenses do not necessarily grant trademark rights for the brands depicted by individual icons. Consumers remain responsible for applicable licenses, attribution requirements, trademark policies, and brand guidelines.

Runtime PNGs are deterministic derivatives created by the user-side builder
from the selected canonical SVG. Rasterization grants no additional copyright,
trademark, endorsement, or redistribution right. A runtime asset inherits the
same selected-source identity, provenance, evidence, notices, and limitations
as its canonical SVG; it is not a separately sourced work.

## Format evolution

Format 1 is frozen and remains the builder default until the OTP Harbor
consumer deliberately ships v2 support. Format 2 must be selected explicitly
with `--format-version 2`. Importers must dispatch on `formatVersion` and reject
unknown versions rather than partially interpreting them. Neither reader may
ignore unknown manifest members or unreferenced files.
