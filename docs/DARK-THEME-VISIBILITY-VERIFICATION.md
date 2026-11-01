# Dark-theme correction verification — 2026-10-31

Builder base commit: `b59e74ae52c0285330dba6cb0ef5c9403522f2c9` plus the
uncommitted dark-theme/source-paint corrections and performance-policy update.
These artifacts are not attributed to an unchanged release commit.

Release solution build: zero warnings/errors. Full test suite: 181 passing.
Synthetic regressions cover dark-blue paint against the actual dark account
surface, adjacent multicolor marks, SVG-default black inside a white badge,
and strict v3 round trips. Existing v1/v2 compatibility tests remain passing.

Two separate complete `build --format-version 3 --offline --non-interactive`
runs produced identical archive, rights-report, and visual-report bytes.
The second run used `--force --output
C:\Users\Admin\Downloads\otp-harbor-icons.otphicons`. Each build reopened and
validated the completed archive and runtime visual references before atomic
publication. An additional final-file audit verified manifest coverage,
canonical paths, every PNG SHA-256, stable timestamps, sorted canonical IDs,
absence of SVG members/files and unreferenced files, and report coverage.

| Measurement | Result |
| --- | ---: |
| Format | 3 |
| Canonical brands | 5,745 |
| Theme appearances | 11,490 |
| Unique PNG assets | 6,083 |
| Unresolved visual failures | 0 |
| Archive bytes | 48,049,868 |
| PNG payload bytes | 24,617,013 |
| Largest PNG bytes | 35,561 |
| Average PNG bytes | 4,046.85 |

The first final-code run took 56.61 seconds using previously downloaded source
archives. This is a processing/reproducibility observation, not a cold-start
benchmark or a promised time limit. Initial network acquisition was not
benchmarked in this task. The requirement is now the fastest practical first
run with high image quality and a responsive computer, without a hard
one-minute deadline. Existing cooperative CPU throttling is retained.

The generated light/dark comparison sheet was inspected for the 15 distinct
visible brands from the supplied screenshots: 1&1, 20i, 2dehands, AI Dungeon,
Air Transat, Akamai, Android, Apache FreeMarker, Appsmith, Bank of America,
Bazarr, Bentley, Booking.com, The Guardian, and The Weather Channel. Bazarr
contains its black/white interior motif in both themes. Low-contrast dark
wordmarks use declared light tiles; Android's dark appearance is transparent.
This comparison uses generated PNGs and manifest backgrounds; it does not
certify a phone's import/cache/rendering state. Device reimport and theme
verification remain consumer acceptance work.

Final local artifacts and lowercase SHA-256:

- `C:\Users\Admin\Downloads\otp-harbor-icons.otphicons`:
  `bc12293d85b503412709b7219c9378b4c63cbcd9be17cddea0ed9b3ff14426e6`
- `C:\Users\Admin\Downloads\otp-harbor-icons.rights-report.json`:
  `cae1a963dc96296fcba4f7465e75b26829b6449f65d12e5c90350ea80203720f`
- `C:\Users\Admin\Downloads\otp-harbor-icons.visual-report.json`:
  `1bb8f0cc81284bc57c57ad4da3f7f974dccc9be59b7c4796c959a7e823ec596f`

No generated pack, downloaded archive, SVG, or PNG is tracked in Git. Artifacts
were neither committed nor published. Consumer source was not modified.
The manifest/profile contract is unchanged; see
[consumer verification handoff](DARK-THEME-VISIBILITY-CONSUMER-TRANSFER.md).
