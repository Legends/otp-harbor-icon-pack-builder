# Format v3 acceptance review

This review records the focused acceptance checks performed before treating the
format-3 contract as provisionally frozen. It is an engineering and provenance
review, not legal advice.

## Catalog coverage and Aegis decision

The corrected pinned-source catalog contains 5,745 canonical brands. Removing
Aegis would remove 459 brands for which neither Dashboard Icons nor Simple Icons
provides a selected replacement. The unique set includes recognizably useful
services such as Best Buy, DocuSign, Heroku, ING, Kraken, 23andMe, ESET,
F-Secure, Interactive Brokers, Mercado Libre, GOV.UK, and login.gov. Aegis is
therefore retained; its contribution is not limited to obscure brands.

Aegis primary artwork is preserved as supplied. Omitted SVG fill is
materialized as SVG-default black before full-composition detection, preventing
black-background/white-mark badges such as OpenAI from becoming solid white
circles. Recognized Aegis variations remain linked for provenance but are not
selected automatically because their palette is not an authoritative,
machine-readable light/dark brand contract.

The same semantic correction is applied to format 2 before its foreground-only
normalization: a full-canvas implicit-black shape is promoted to the manifest
background and the white mark remains in the PNG. Format 1 remains unchanged
and relies on standard SVG default-fill behavior in the consumer.

## Brand-count discrepancy

The apparent extra brand was `fenrus`. Dashboard metadata identified it as a
non-SVG record that was also referenced as another record's SVG variant. The
acquisition/provider pipeline incorrectly promoted that variant asset to a
canonical brand. Metadata filtering now uses only default SVG records and the
provider independently rejects non-SVG top-level records. The acquisition cache
contract was bumped so stale affected metadata cannot be reused.

## Theme behavior

Complete compositions use `assetRole: complete-icon` and `background.mode:
embedded`; the consumer must not add a second branded background. Transparent
foreground artwork uses its declared solid tile. Unpaired/default artwork is
kept on a neutral white tile in both themes. A dark tile is used only for a
validated provider-supplied dark-theme appearance. Dashboard self-references are
not treated as native theme variants.

Theme switching selects the resolved manifest appearance. It does not require a
pack reimport or eager decoding of every PNG. The consumer can logically key by
brand, theme, and hash while physically deduplicating decoded bitmaps by hash.

## Archive overhead

The release-candidate v3 measurement contains 24,726,615 bytes across 6,091
unique PNG assets; the complete archive is 48,112,413 bytes. The gap is overwhelmingly
manifest content, not ZIP directory overhead or PNG compression. Manifest
compaction is a future format design opportunity; changing it is not justified
during provisional contract freeze without a measured consumer requirement.

## Visual review

The automated gate checks both appearances for every brand and remains
mandatory. A representative contact sheet is additionally generated outside
the repository for manual review. The review specifically includes native
Dashboard pairs, unpaired multicolor/default artwork, Simple Icons foreground
color, and Aegis complete compositions such as OpenAI and Auth0. Generated
packs and contact sheets are local artifacts and are not committed or
published.

The follow-up review also covers the explicitly mapped AdAway, AdBlock,
Adblock Plus, 2K, and .ENV compositions on light and dark application surfaces.
Their source brand paint and opaque white internal details are preserved, while
the exterior and its antialiased boundary remain transparent. Format-3 assets now use an
eight-pixel safe inset; wide about.me and Ableton artwork was inspected to
confirm it no longer touches the tile edge. A separate non-mutating Simple
Icons topology audit found 1,039 possible enclosed-region candidates, but only
the five source-reviewed badges were changed because topology cannot establish
the intended paint of ordinary logo holes.

Dashboard and Aegis source artwork with a large source-painted backplate that
contains contrasting detail is classified generically as a complete embedded
composition. The detector does not fill holes or synthesize a background.
A deterministic 36-brand sample was reviewed after the full build. The pinned
catalog contains 1,461 brands meeting this structural rule (1,592 Aegis and
1,307 Dashboard appearance references); adjacent multicolor marks remain
foreground artwork.

Aboard and Acronis exposed a separate quality defect: their complex source
paint required the audit SVG's 24-pixel raster fallback, which was then enlarged
for the runtime asset. Format 3 now retains a bounded canonical 128-pixel RGBA
normalization result and verifies the final PNG directly against the validated
source. This removes that upscale while keeping the strict source-to-PNG gate.
The final-size path is limited to text-free artwork. SVG text remains on the
canonical raster-SVG fallback because system font resolution and glyph hinting
produced measurable cross-run byte differences in `open-regex` and `remuxcode`.

Two complete revised builds, visual reports, and rights reports are
byte-identical after excluding SVG text from the final-size native-raster path.
The final release-candidate archive contains 5,745 brands, 11,490 appearance
references, 6,091 unique PNGs, zero SVGs, and 5,399 shared references. Its
24,726,615-byte PNG payload averages 4,059.53 bytes; the largest PNG is 35,561
bytes. The archive is 48,112,413 bytes. SHA-256 values are:

- pack: `c3d20ef87da8fc61a8a24bc00c5b97e862ce0248c02839ef6c4f72e66a0de807`;
- visual report: `a0f83f23a1b43b3f71ddd3758b3c8da5b5fa84ffbfca7e57b12aab606a849c81`;
- rights report: `cae1a963dc96296fcba4f7465e75b26829b6449f65d12e5c90350ea80203720f`.

The visual report has zero unresolved failures.

## Deferred enrichment sources

Additional identity databases such as SVGL or commercial brand APIs are not
part of the frozen v3 implementation. They require a separate coverage,
security, provenance, maintenance, and redistribution-rights review before
their metadata or artwork can enter a user-generated pack.
