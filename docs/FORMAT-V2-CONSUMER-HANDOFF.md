# Format v2 consumer handoff

This is the implementation contract for adding `.otphicons` format version 2
to OTP Harbor. The builder implementation does not modify the OTP Harbor
repository.

## Transactional import

OTP Harbor must continue to accept strict format 1 and add a separate strict
format-2 validation path. It must stage and completely validate a candidate
pack before replacing an installed pack. A failure must leave the previous
installation and its cache-revision state untouched.

For every v2 brand, install these as one transaction:

- `icons/<canonical-brand-id>.svg`;
- `runtime/foreground-128/<canonical-brand-id>.png`;
- the corresponding manifest metadata, provenance, aliases, and
  `backgroundColor`.

Reject missing, additional, unreferenced, duplicate-normalized, conflicting,
absolute, traversing, backslash, oversized, or decompression-bomb entries.
Retain all current limits and checks for manifest shape, canonical IDs,
aliases, sources, licenses, and normalized SVGs. In addition, validate the
runtime PNG signature and chunk structure, RGBA profile, 128 by 128 decoded
dimensions, encoded/decoded budgets, exact canonical path, media type,
`otp-harbor-foreground-v1` profile, and lowercase SHA-256 over the packaged
bytes. Reject unknown manifest members and unsupported format versions.
PNG image data uses a deterministic standards-compliant zlib/DEFLATE stream
with filter type 0; consumers must decode it normally and must not assume that
IDAT contains uncompressed DEFLATE stored blocks.

## Display path

Use the canonical brand ID plus the runtime SHA-256 as the cache revision key,
for example `github:<sha256>`. The hash changes when the display bytes change
without making provider identities part of application behavior.

Decode the PNG outside the UI thread. Maintain a bounded bitmap memory cache
with an explicit eviction policy appropriate to the application process. List
binding, including fling/fast scrolling, should only look up and bind a cached
bitmap or enqueue an asynchronous PNG decode. It must not parse, normalize,
interpret, transform, recolor, or rasterize SVG in that path.

Draw the manifest `backgroundColor` as the tile surface and place the runtime
PNG as the transparent foreground using the existing 40 by 40 tile / centered
24 by 24 foreground semantics. Do not infer a background from pixels and do
not recolor explicit foreground colors.

Canonical SVG fallback is limited to:

- normal display of a strict v1 pack; or
- exceptional recovery when a previously validated v2 runtime cache entry is
  unavailable or a platform decode fails.

Recovery must not weaken import validation. A corrupt v2 archive is rejected;
it is not accepted as a v1-like pack. Any SVG fallback work must remain off the
UI thread and outside list binding.

## Boundaries that remain in the builder

OTP Harbor must never parse Aegis Icons, Simple Icons, or Dashboard Icons
metadata, choose among providers, download icon sources, access the network for
icon resolution, derive canonical identities, merge aliases, choose colors, or
generate runtime assets. Those are builder responsibilities. The installed
pack remains fully local-first.

## Cache lifecycle

On successful pack replacement, retain cache entries only when their complete
`canonical-brand-id:runtime-sha256` revision key is still referenced. Evict old
revisions lazily or during the install transaction without exposing a partially
installed catalog. Bound both entry count and memory bytes; do not assume 100
decoded 128 by 128 images are free (RGBA storage is 64 KiB per bitmap before
platform overhead).

## Compatibility rollout

The coordinated release sequence is:

1. ship and test strict v2 importer/storage/cache/display support in OTP Harbor;
2. verify strict v1 import remains unchanged;
3. exercise transactional upgrade, rollback, and corrupted-v2 cases;
4. only then change the builder's default from format 1 to format 2 in a small,
   intentional release change.

Until that consumer release exists, users must request builder v2 explicitly
and heed its compatibility notice.
