# `.otphicons` icon pack format

This document describes the provider-neutral archive produced by OTP Harbor Icon Pack Builder. It is intended for pack inspection, validation, and compatible importer implementations.

## Container

An `.otphicons` file is a ZIP archive with this layout:

```text
pack.json
icons/
  <canonical-brand-id>.svg
licenses/
  aegis/
  dashboard-icons/
  simple-icons/
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

The normative machine-readable definition is [`../schemas/otp-harbor-icon-pack.schema.json`](../schemas/otp-harbor-icon-pack.schema.json).

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

## Visual report

A successful build also writes a sibling diagnostic file:

```text
otp-harbor-icons.otphicons
otp-harbor-icons.visual-report.json
```

The visual report contains per-brand source and normalized palettes, operations, and pixel-comparison metrics. It is not part of format 1 and is not an import input. A successful catalog has `unresolvedFailures: 0`.

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

Validation and installation should be transactional: fully stage and validate the new pack before replacing any prior pack.

## Licensing and provenance

The archive preserves source revisions, hashes, metadata, selected-source provenance, and available license or attribution files. These records must remain associated with an installed or redistributed pack.

Repository licenses do not necessarily grant trademark rights for the brands depicted by individual icons. Consumers remain responsible for applicable licenses, attribution requirements, trademark policies, and brand guidelines.

## Format evolution

Incompatible changes require a new numeric `formatVersion`. Importers should explicitly reject unknown versions rather than partially interpreting them. Additive metadata that does not alter required validation semantics may remain within the current version when allowed by the schema.
