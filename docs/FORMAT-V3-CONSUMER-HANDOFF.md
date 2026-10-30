# Format v3 consumer handoff

Format 3 is the current, coordinated consumer contract and the builder default.
Format 1 and format 2 remain frozen and explicitly selectable for legacy
consumers. A caller may still request v3 explicitly with `--format-version 3`.

## Transactional import

Dispatch strictly on `pack.json.formatVersion`. Stage the complete archive,
validate it, and install it atomically. Never reinterpret a malformed v3 pack
as v1 or v2. A valid archive contains only `pack.json`, referenced license
files, and referenced `runtime/appearance-128/<sha256>.png` files. It contains
no SVG files, `icon` members, top-level `backgroundColor`, or `runtimeIcon`.

Every brand has exactly two resolved manifest appearances: `lightTheme` and
`darkTheme`. The two descriptors may reference the same content-addressed PNG.
Install both descriptors and their referenced file in one transaction.

## Appearance binding

For the current application theme, select the corresponding appearance before
binding the row:

- `assetRole: foreground` requires `background.mode: solid`. Draw the declared
  color as the tile surface, then center the transparent PNG above it.
- `assetRole: complete-icon` requires `background.mode: embedded`. Draw the PNG
  as the complete composition and do not add a second branded background.

The builder may use an explicitly reviewed badge composition to preserve an
authentic colored badge with opaque internal details and a transparent
exterior. This is still an ordinary `complete-icon`/`embedded` appearance to
the consumer. Do not add a white tile behind it or attempt to reconstruct,
fill, or reinterpret transparent regions.

Do not invert, tint, recolor, or otherwise reinterpret the PNG. A subtle
application-owned outline may be drawn outside the artwork when the surrounding
surface needs separation, but it must not replace or modify the declared
appearance.

An unpaired/default foreground can intentionally declare the same neutral white
tile in both themes. Do not replace it with the application's dark surface. A
dark tile is emitted only when the selected source artwork is explicitly suited
to the dark application theme. Theme changes select another manifest descriptor
and do not require reimporting the pack or decoding every asset eagerly.

The builder has already resolved provider-specific conventions. The consumer
must not parse Dashboard Icons, Simple Icons, or Aegis metadata.

## Asset validation and caching

Before installation validate:

- exact lowercase content-addressed path
  `runtime/appearance-128/<sha256>.png`;
- `image/png`, 128 by 128, and render profile
  `otp-harbor-themed-appearance-v1`;
- lowercase SHA-256 against the packaged bytes;
- PNG signature, 8-bit RGBA, non-interlaced IHDR, contiguous bounded IDAT data,
  IEND, permitted chunk ordering/types, and absence of ancillary chunks,
  without eagerly decoding the complete catalog;
- valid role/background metadata for every appearance;
- exactly two valid appearances per brand and no unreferenced archive file.

Use `<canonical-brand-id>:<theme>:<asset-sha256>` as the logical cache revision
key. The decoded bitmap cache may deduplicate physically by SHA-256 because both
themes or multiple brands can reference identical bytes. Decode outside the UI
thread, maintain a bounded memory cache, and bind cached bitmaps during fling.
No provider parsing, network access, SVG parsing, or SVG rasterization belongs
on the consumer display path.

Catalog-wide semantic pixel checks—including blank/covered output, alpha
bounds, clipping, layer order, color fidelity, and foreground/background
visibility—are mandatory builder publication gates. The consumer verifies the
manifest, hashes, and bounded PNG container structure transactionally, but does
not inflate every PNG during import to repeat visual analysis. Validate normal
decoder invariants when an asset is decoded on demand outside the UI thread;
an on-demand decode failure uses the installed-asset recovery path.

## Provenance and recovery

Each appearance identifies the provider source record and source asset path
chosen by the builder and includes an `appearanceSelectionReason`. Preserve
these diagnostics with the installed pack. Runtime PNGs are deterministic
derivatives of the recorded upstream artwork and inherit its rights evidence;
rasterization grants no additional copyright or trademark rights.

Keep strict v1 and v2 support. Fall back to an older installed pack only as a
transactional recovery action. A missing or corrupt v3 PNG is an import/storage
failure, not permission to fetch a provider asset or render an unavailable SVG.
