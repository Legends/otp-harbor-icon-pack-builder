# OTP Harbor Codex transfer prompt — revised format-v3 badge and inset contract

Work in the OTP Harbor / TOTP-Manager repository. Read and follow its complete
`AGENTS.md` before changing anything.

Do not modify the Icon Pack Builder repository from this task. The builder-side
implementation and its generated validation artifacts already exist.

## Objective

Update OTP Harbor's strict format-version-3 importer/model validation to accept
the revised provisional v3 appearance contract produced by OTP Harbor Icon Pack
Builder.

This is an additive consumer compatibility update. Do not weaken archive,
manifest, PNG, hash, path, size, or transactional-install validation.

## Contract changes to consume

### 1. New appearance selection value

Format-3 `lightTheme` and `darkTheme` appearance descriptors may now use:

```json
"selection": "reviewed-badge-composition"
```

Add this value wherever OTP Harbor strictly enumerates valid format-3
appearance-selection classifications. Preserve all previously supported values.
Continue rejecting unknown values.

`selection` is diagnostic provenance. It does not override `assetRole` or
`background` rendering semantics.

### 2. Reviewed badge appearance semantics

The initial reviewed-matte brands are AdAway, AdBlock, Adblock Plus, 2K, and
.ENV. For both themes, the builder emits:

```json
{
  "assetRole": "complete-icon",
  "background": {
    "mode": "embedded"
  },
  "selection": "reviewed-badge-composition"
}
```

The PNG already contains the complete authentic composition:

- original red badge paint;
- opaque white internal bird, hand, or ABP details;
- transparent exterior, including antialiased edges.

OTP Harbor must render this exactly like every other
`complete-icon`/`embedded` appearance:

- draw the PNG as supplied;
- do not draw an additional white, rounded-square, branded, or theme tile;
- do not fill transparent regions;
- do not tint, invert, recolor, crop, or reconstruct the badge;
- do not special-case the three brand IDs.

The manifest role/background pair remains the sole rendering instruction. The
new selection value is not a request for consumer-side image processing.

### 3. Normative format-3 safe inset

The builder now fits all format-3 visible artwork inside a centered 112 by 112
content area on the 128 by 128 transparent PNG canvas. Pixels 0–7 and 120–127
form the maximum safe inset around full-width/full-height artwork. Aspect ratio
and centering are preserved.

This inset is already baked into the PNG. OTP Harbor must:

- continue drawing the complete 128 by 128 bitmap into its normal icon bounds;
- not add another content inset or compensate by zooming/cropping;
- not trim transparent PNG bounds during import or display;
- retain normal density-aware bitmap scaling at presentation time.

The render profile string remains:

```text
otp-harbor-themed-appearance-v1
```

Formats 1 and 2 are unchanged. Do not apply the format-3 inset as an additional
consumer transformation to any format.

## Existing behavior that must remain

- Dispatch strictly on `pack.json.formatVersion`.
- Preserve strict v1 and v2 support.
- Require exactly `lightTheme` and `darkTheme` for each v3 brand.
- Require `foreground` with `background.mode: solid` and an uppercase color.
- Require `complete-icon` with `background.mode: embedded` and no color.
- Validate the exact content-addressed path
  `runtime/appearance-128/<lowercase-sha256>.png`.
- Validate SHA-256, bounded PNG container structure, 128×128 8-bit RGBA
  non-interlaced IHDR, permitted chunk ordering/types, encoded-size limits,
  entry coverage, duplicate paths, Zip Slip, archive limits, and no
  unreferenced files without eagerly decoding the complete catalog.
- Install the manifest and referenced PNG assets transactionally.
- Decode outside the UI thread and preserve the bounded bitmap memory cache.
- Use canonical brand ID, theme, and runtime SHA-256 as the logical cache
  revision key; physical bitmap deduplication by SHA-256 remains valid.
- On theme changes, select the corresponding installed appearance without
  reimporting the pack or eagerly decoding the complete catalog.
- Never parse provider metadata, render SVG, or access the network in the
  consumer display/import path.

The builder owns catalog-wide semantic pixel validation, including blank or
fully covered output, foreground/background visibility, alpha bounds, clipping,
paint/layer fidelity, and source-to-runtime comparison. OTP Harbor must not
inflate or decode every PNG during import merely to repeat these visual gates.
When a bitmap is later decoded on demand, treat an ordinary decoder failure as
a corrupt installed asset and use the existing recovery path; do not move a
full-catalog visual-analysis pass back into import.

## Required implementation approach

1. Inspect the current v3 manifest models, strict JSON parsing, import
   validation, persistence, cache keys, and list-row rendering before editing.
2. Make the smallest coherent consumer change. In a correctly role-driven
   renderer, only the strict selection enumeration/schema may require code
   changes; do not add brand-specific rendering logic.
3. If the consumer currently adds a background to `complete-icon` assets, fix
   that generic contract violation instead of special-casing these brands.
4. If the consumer trims transparent bitmap bounds or adds its own foreground
   padding, remove that reinterpretation for v3 and render the supplied canvas
   as-is.
5. Keep importing and displaying older valid v3 packs that use the existing
   selection values.

## Tests

Use synthetic PNGs and manifests only. Do not commit the generated third-party
pack or copied upstream artwork.

Add or update focused tests proving:

- `reviewed-badge-composition` is accepted for both theme appearances;
- unknown selection strings remain rejected;
- `complete-icon`/`embedded` draws no additional background;
- an on-demand decoded synthetic complete icon preserves transparent exterior
  pixels on light and dark application surfaces;
- the full 128×128 canvas is retained without transparent-bound trimming;
- no second consumer inset is added to a PNG whose artwork already occupies
  pixels 8–119;
- light/dark appearances may reference the same content-addressed asset;
- theme switching selects descriptors without reimporting;
- cache revision behavior remains based on the runtime hash;
- v1/v2 tests remain unchanged and passing;
- malformed role/background combinations and unknown manifest members remain
  rejected.

Run the full OTP Harbor build and test suite. If Android UI or screenshot tests
exist, add a synthetic complete badge on both light and dark surfaces and a
wide wordmark with the baked-in safe inset.

## Acceptance evidence from the builder

The builder produced and reopened two byte-identical full v3 archives:

- 5,745 brands;
- 11,490 theme appearances;
- 6,091 unique PNG assets;
- zero unresolved visual failures;
- archive SHA-256:
  `c3d20ef87da8fc61a8a24bc00c5b97e862ce0248c02839ef6c4f72e66a0de807`.

The contract-aware contact sheet confirms the reviewed badges on both light and
dark application surfaces. The pack and reports are local validation artifacts
and must not be committed or published from the OTP Harbor repository.

## Completion report

Report:

- files changed;
- whether rendering was already fully role-driven;
- exact validation changes made for the new selection value;
- tests added and full test totals;
- confirmation that v1/v2 behavior remains intact;
- confirmation that no generated third-party pack or upstream artwork is
  tracked;
- any remaining consumer compatibility concern.
