# Format v2 consumer handoff

This is the implementation contract for adding the unreleased PNG-only
`.otphicons` format version 2 to OTP Harbor. The builder implementation does
not modify the OTP Harbor repository.

## Version dispatch and transactional import

OTP Harbor must preserve its strict SVG-only format-1 path and add a separate
strict format-2 path. Dispatch only on `pack.json.formatVersion`; never retry a
malformed v2 archive as v1. Stage and completely validate a candidate before
replacing the installed catalog. Failure or cancellation must leave the prior
catalog and cache-revision state untouched.

For every v2 brand, install as one transaction:

- `runtime/foreground-128/<canonical-brand-id>.png`;
- `runtimeIcon`, aliases, provenance, rights metadata, and `backgroundColor`.

Format 2 contains no `icon` manifest member and no SVG archive entry. Reject an
`icon` member even when it is null. Reject every SVG, provider raster, source
file, report, or other unreferenced archive file. Only `pack.json`, one runtime
PNG per brand, deterministic directory entries, and license/notice files
referenced by `sources[].licenseFiles` are permitted.

Reject missing, additional, duplicate-normalized, case-colliding, conflicting,
absolute, traversing, backslash, oversized, or decompression-bomb entries.
Retain current manifest, canonical-ID, alias, source, provenance, license,
entry-count, compressed-size, expanded-size, and compression-ratio limits.
Reject unknown manifest members and unsupported versions.

For every runtime PNG validate once, before installation:

- exact path `runtime/foreground-128/<canonical-brand-id>.png` and one-to-one
  brand/PNG coverage;
- `image/png`, 128 by 128, and `otp-harbor-foreground-v1` metadata;
- lowercase SHA-256 over the exact packaged bytes;
- PNG signature and CRCs, exactly one `IHDR`, contiguous `IDAT`, exactly one
  final `IEND`, and no trailing bytes or ancillary chunks;
- 8-bit RGBA/color type 6, compression method 0, PNG filter method 0,
  non-interlaced, and filter byte 0 on every decoded scanline;
- at most 1 MiB encoded and exactly 65,536 decoded RGBA bytes;
- at least one nontransparent pixel and visible separation from
  `backgroundColor`.

The image data is a standards-compliant deterministic zlib/DEFLATE stream.
Consumers must support valid compressed streams and must not assume stored
DEFLATE blocks.

The archive remains untrusted even though the builder validates its own output.
After the importer has completely validated a staged PNG, the installer may
stream those already validated bytes into durable storage without decoding them
a second time, but it must recompute SHA-256 while copying and compare it with
the validated manifest. This optimization does not remove or weaken the first
bounded decode/validation pass.

## Display path

Use `<canonical-brand-id>:<runtime-sha256>` as the cache revision key. Decode
PNG bytes outside the UI thread. Maintain a bitmap cache bounded by both entry
count and memory bytes. A decoded 128 by 128 RGBA bitmap costs 64 KiB before
platform overhead, so an 8 MiB pixel budget holds at most about 128 images.

During fling or normal list binding, only bind a cached bitmap or enqueue an
asynchronous PNG decode. Draw manifest `backgroundColor` as the 40 by 40 tile
surface and place the transparent PNG in the centered 24 by 24 foreground
square. Do not infer or bake the background and do not recolor explicit
foreground colors.

OTP Harbor must not parse, normalize, transform, recolor, or rasterize SVG for
v2 because no SVG exists in a valid v2 pack. SVG rendering remains solely the
normal strict-v1 display path. A missing or corrupt v2 PNG is an import/storage
failure, not permission to seek an SVG fallback or contact a provider.

## Boundaries that remain in the builder

OTP Harbor never parses Aegis Icons, Simple Icons, or Dashboard Icons metadata,
chooses among providers, downloads sources, accesses the network for icon
resolution, derives canonical identities, merges aliases, chooses colors, or
generates runtime assets. Provider SVG safety validation, normalization, source
selection, reference rendering, and PNG generation remain builder-internal.
Automatic lookup uses only the mandatory TOTP issuer; the optional account name
is never a brand-resolution input.

## Cache lifecycle

After successful replacement, retain cache entries only while their complete
brand/hash revision key remains referenced. Evict stale revisions lazily or in
the install transaction without exposing partial state.

## Compatibility rollout

1. Ship and test strict PNG-only-v2 importer/storage/cache/display support.
2. Verify frozen v1 import and SVG display remain unchanged.
3. Exercise transactional upgrade, rollback, malformed-PNG, and corrupted-v2
   cases.
4. Retain explicit format selection after newer coordinated formats become the
   builder default.

The coordinated theme-aware release has since made format 3 the builder
default. Format 2 remains frozen and available through `--format-version 2` for
consumers that implement this contract.
