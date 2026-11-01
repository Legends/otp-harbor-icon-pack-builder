# 9router format-3 raster correction — 2026-11-01

Version 3.0.1 uses the same pinned Dashboard Icons source snapshot as 3.0.0.
The selected `dashboard-icons/9router` SVG includes a `<text>` element. The
previous format-3 runtime asset enlarged a 24-pixel canonical raster to
128 pixels, softening the white `9R` glyphs and introducing horizontal bands.

The reviewed 9router source now retains its source-to-canonical visual audit
and captures its source pixels at the final 128-pixel, eight-pixel-inset
geometry before deterministic PNG encoding. A broader text-source experiment
was rejected because Sonarr DV produced different bytes across two builds.
The final change is scoped to 9router. Format 1/2 rendering and other text
sources retain their existing behavior.

The release solution builds with zero warnings/errors; all 196 tests pass.
The new synthetic regression asserts that the packaged 9router-style PNG
matches the bounded final-size source RGBA byte for byte. The complete pinned
format-3 pack reopens with 5,745 brands, 11,490 theme appearances, and zero
unresolved visual failures. Compared with the previously verified pack, only
the `9router` brand descriptor changes. Its old PNG asset hash was
`3166d821fdc2f9e7165dbf023fb98baafbd2cdc31244e6de7ee41afad4c25d8b`;
the new hash is
`cd11607186c90e02f2895e5a3f23e4de15b24663fa890095729c5ae2d4f4176b`.
The prior and corrected packaged PNGs were visually compared at enlarged size
in `out/9router-before-after.png`.

Two independent offline runs using cached pinned sources produced identical
pack and report bytes:

| Local artifact | SHA-256 |
| --- | --- |
| `out/9router-scoped-a.otphicons` | `d04dea2885d586060c1b2e2604138b0201650aafe06ffdba232afcf57ed01abb` |
| `out/9router-scoped-a.visual-report.json` | `7111c529554f9d709df16fc63011a2618089df8a3da036be55efd7dfbdff96b7` |
| `out/9router-scoped-a.rights-report.json` | `cae1a963dc96296fcba4f7465e75b26829b6449f65d12e5c90350ea80203720f` |

The source-controlled schemas and mappings parse successfully. No generated
pack, source archive, or icon tree is tracked. The release workflow publishes
only executables, launchers, documentation, notices, and checksums. Users must
build their own updated pack and reimport it. See the
[consumer transfer prompt](9ROUTER-CONSUMER-TRANSFER.md). The manifest schema,
render profile, appearance descriptors, and consumer fitting rules are
unchanged; only 9router's packaged PNG bytes/content-addressed path change.
