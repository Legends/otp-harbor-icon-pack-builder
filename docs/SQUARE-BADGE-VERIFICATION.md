# Square badge verification — 2026-11-01

Builder base commit: `b59e74ae52c0285330dba6cb0ef5c9403522f2c9`, plus the
existing uncommitted dark-theme corrections and square-frame implementation.
These artifacts are not attributed to the unchanged release commit.

The Release solution builds with zero warnings/errors. All 195 tests pass,
including sharp frame corners, every exterior/frame pixel, opaque white
details, retained blue paint, antialiased interior edges, both-theme sharing,
rejected circle/rounded/rectangular/notched geometry, missing brand paint,
invalid mapping modes/colors/rationales, cancellation, strict pack round trips,
deterministic archive bytes, and frozen v1/v2 compatibility. The real embedded
1&1 and AI Dungeon mappings are exercised using synthetic geometry. Their
pinned sources were inspected locally and both define full square silhouettes.

Two complete `build --format-version 3 --offline --non-interactive` runs
produced identical pack, visual-report, and rights-report SHA-256 values.
Each build reopened and validated the archive before publication. A separate
audit checked every content-addressed PNG hash/path and all four badges' full
frame, opaque interior, and transparent exterior. All source-controlled schema
and mapping JSON files parse; mapping/schema regression tests pass.

| Measurement | Result |
| --- | ---: |
| Canonical brands | 5,745 |
| Issuer aliases | 7,767 |
| Theme appearances | 11,490 |
| Unique PNGs | 6,083 |
| Unresolved visual failures | 0 |
| Archive bytes | 48,054,116 |
| PNG payload bytes | 24,619,553 |
| Largest PNG bytes | 35,561 |

Local final artifact: `C:\Users\Admin\Downloads\otp-harbor-icons-square-badges.otphicons`.
Reports use the same basename with `.visual-report.json` and
`.rights-report.json`. This replaces the earlier square-badge artifact at that
path; the original `otp-harbor-icons.otphicons` was retained.

- Pack SHA-256: `4712715b0bb78863f52a1888982c42c6de6b0d053db7e0f437881ce214565412`
- Visual report SHA-256: `a8499dd81102bb26abae1de12c90d5f9d439ef3855baa3067b59deeac49cbe7d`
- Rights report SHA-256: `cae1a963dc96296fcba4f7465e75b26829b6449f65d12e5c90350ea80203720f`

The comparison build is `out/square-badge-expanded-verification.otphicons`. Its preview
is `out/square-badge-expanded-preview.png`, created from packaged PNGs on light `#FFFFFF`
and dark `#10213F` surfaces at 128 px and 26 px. Visual inspection confirmed
blue/black paint, opaque white lettering, sharp corners, and a thin white frame.
The frame naturally blends into the light surface. Booking.com's asset hash
is `b73e6b8566a41e2949075ec349945cff5600eb12d4fbc5b1ff0b82b883e61a45`;
The Weather Channel's is
`8b6b97998d41d0d7916641474bbc7f52eaf0563390da55c05d2d70f67738cff3`.
Each brand shares its PNG between both themes.

Compared with the previous pack retained at
`out/square-badge-verification.otphicons` (SHA-256
`9fb7b041c011952747da2cc13c6e7fa662725994626c6a22b8c3001ec733a3a1`), only
the `1-1` and `ai-dungeon` brand descriptors changed. Booking.com and The Weather
Channel retain their prior frame assets. The rights report remains identical.

Both runs reused pinned cached sources. No cold-acquisition measurement or
first-run timing claim is made. No downloaded source collection or generated
pack/PNG/SVG is tracked, committed, or published. Consumer source is unchanged.

Consumer-visible raster fitting uses the same treatment for all four reviewed badges:
104-pixel source square, four-pixel frame, existing eight-pixel transparent
inset. Manifest fields, enum values, render profiles, runtime paths, and
consumer fitting rules remain unchanged. 1&1 and AI Dungeon now use
`complete-icon`/`embedded` in both themes, replacing their former dark-theme
`foreground`/`solid` descriptor; the white tile is included in the PNG and must
not be added again by a consumer. Device reimport/theme acceptance
is unverified; use the self-contained
[consumer transfer prompt](SQUARE-BADGE-CONSUMER-TRANSFER.md).
