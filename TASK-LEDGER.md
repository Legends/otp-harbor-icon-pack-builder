# Format v3 themed-appearance implementation ledger

## Format v3 default (2026-10-30)

1. **DONE** — Omitting `--format-version` now produces strict format 3.
2. **DONE** — Formats 1 and 2 remain explicitly selectable and their frozen
   schemas, archive layouts, readers, and serialization behavior are unchanged.
3. **DONE** — CLI help, product specification, README, format documentation,
   and consumer handoffs identify v3 as the current default.
4. **DONE** — CLI regression coverage verifies default v3 output and explicit
   v1/v2 output.
5. **DONE** — Project version is 3.0.0 and the tag-triggered release workflow
   exercises the default strict v3 production build on every release platform;
   generated third-party packs are validation artifacts only and are never
   uploaded to the GitHub release.

## Concise console diagnostics (2026-10-28)

1. **DONE** — Normal interactive output no longer repeats the introductory
   pinned-source sentence or exposes cache-internal skipped-SVG report paths.
2. **DONE** — Source identities are labeled by meaning: Aegis release,
   Dashboard Icons revision, and Simple Icons version.
3. **DONE** — The pack output, compact catalog count, rights-report path, and
   legal notice remain visible. Visual-report paths and detailed provider,
   selection, skip, and rights statistics are available through `--verbose`.
4. **DONE** — Reports are still generated and transactionally published in all
   modes; verbosity affects console presentation only. Synthetic CLI tests
   cover both concise and verbose output.

## Pixel-checked Dashboard variants and issuer-alias audit (2026-10-27)

1. **DONE** — Dashboard's declared theme variant remains the first candidate,
   but its actual final-size RGBA must contrast with the intended application
   surface. If it does not, the builder deterministically tries the other
   provider-supplied variant before the default artwork. This fixes the pinned
   `.NET` metadata inconsistency without a brand-specific rendering override.
2. **DONE** — A synthetic regression proves that incorrectly declared black
   dark-theme artwork is rejected in favor of the native white variant, and
   vice versa for the light theme. The complete pinned build now selects
   `dotnet-light` for light theme and the white `dotnet-dark` artwork for dark
   theme. Opposite-theme fallbacks are considered only when an intended variant
   was actually declared and failed, preventing guesses from lone variants.
3. **DONE** — Exact, collision-free aliases were added for the core Microsoft
   product family, including Outlook, Word, Excel, PowerPoint, OneDrive,
   OneNote, Access, Exchange, SharePoint, Teams, Windows, Office, Intune,
   Defender, Dataverse, and Power Automate. Tests verify representative aliases
   are present in the emitted normalized lookup index.
4. **DONE** — The broader vendor-prefix audit did not automatically claim
   ambiguous words such as `Maps`, `Cloud`, `Music`, or `Calendar`. Canonical
   duplicates such as Azure/Microsoft Azure, Bing/Microsoft Bing, and the
   Atlassian-prefixed product pairs require explicit identity review instead
   of an unsafe alias winner.
5. **DONE** — Empty or whitespace-only SVG `<text>` elements no longer classify
   artwork as font-dependent. The 4chan Dashboard source therefore renders
   directly at final 128-pixel resolution instead of taking the former
   24-to-128 raster fallback; meaningful text still retains the deterministic
   font-safety path.
6. **DONE** — Release build succeeds with zero warnings and all 176 tests pass.
   The completed V3 pack reopens successfully with 5,745 brands, 7,767 aliases,
   11,490 appearances, and zero unresolved visual failures. The V3 consumer
   contract is unchanged. A second full build produced byte-identical pack,
   visual-report, and rights-report files.
7. **DONE** — The canonical `7zip` brand has an explicit Simple Icons source
   override, restoring the exact appearance used immediately before Dashboard
   became the default first-priority provider. Both themes reproduce the former
   `146a3b00...` asset and its foreground-on-white semantics byte-for-byte;
   Dashboard remains the default for every brand without an explicit override.
8. **DONE** — Reviewed source overrides now support optional light/dark
   foreground paint with mandatory rationale. `about.me` retains `#333333` and
   `abuse.ch` retains `#00465B` in light theme; both use opaque white artwork
   with transparent exteriors in dark theme. No background tile is embedded or
   requested, and V1/V2 selection/rendering remains unchanged.

Evidence: 6,091 unique PNGs; 24,726,615 PNG bytes; 35,561-byte largest PNG;
4,059.53-byte average PNG; 48,112,413-byte archive. Pack SHA-256
`c3d20ef87da8fc61a8a24bc00c5b97e862ce0248c02839ef6c4f72e66a0de807`;
visual report SHA-256
`a0f83f23a1b43b3f71ddd3758b3c8da5b5fa84ffbfca7e57b12aab606a849c81`;
rights report SHA-256
`cae1a963dc96296fcba4f7465e75b26829b6449f65d12e5c90350ea80203720f`.

## Bounded-performance and transparent-mark revision (2026-10-27)

1. **DONE** — Full v3 work is performed only for the first usable statically
   ranked source; unselected candidates are processed only as deterministic
   failure fallbacks. The selected safe source is rendered once at the final
   128-pixel/eight-pixel-inset geometry, and its exact RGBA is encoded, reopened,
   and compared byte-for-pixel.
2. **DONE** — Catalog work uses three-eighths of available logical processors.
   Windows and Linux builds sample total system load, begin throttling at 70%,
   and resume below the 65% hysteresis target. Throttling is silent and honors
   cancellation.
3. **DONE** — Independent providers acquire concurrently and Dashboard's pinned
   SVG requests use a bounded 32-request I/O window. Results, diagnostics, cache
   publication, and archive assembly retain deterministic provider/path order.
4. **DONE** — Dashboard Icons is the first default provider. Explicit overrides
   remain authoritative; visual quality ranks candidates within each provider,
   and an unusable preferred source falls back deterministically.
5. **DONE** — Simple Icons marks that are actually visible on a theme surface
   are emitted as transparent `complete-icon`/`embedded` appearances; a solid
   tile is retained only where the materialized brand paint needs contrast.
   This generic rule removes the Android Studio tile without brand-specific
   artwork mutation.
6. **DONE** — Root `currentColor`, transparent root fill, and post-render
   foreground/background collision cases are covered by synthetic regressions.
   The manifest receives the same pixel-checked background used by the renderer.
7. **DONE** — A forced-refresh first run completed in 58.90 seconds on the test
   machine, including all network acquisition, validation, catalog construction,
   archive publication, and reopen; maximum measured builder CPU was 26.8%.
   A no-network full rebuild completed in 18.21 seconds at 29.4% maximum builder
   CPU. Network conditions remain external, so the builder cannot guarantee a
   wall-clock bound independent of the upstream hosts.
8. **DONE** — All 171 tests pass, including byte-identical single-worker versus
   four-worker v3 packaging. Two final offline builds and their visual and
   rights reports are byte-identical. The final visual report records zero
   unresolved failures.
9. **DONE** — V1/v2 schemas and rendering contracts remain unchanged. V3 field,
   role, background, hash, and render-profile semantics are unchanged, so no OTP
   Harbor consumer update or new transfer prompt is required.

Final evidence: 5,745 brands; 11,490 appearance references; 6,157 unique PNGs;
24,940,119 PNG bytes; 35,561-byte largest PNG; 4,050.69-byte average PNG;
48,342,666-byte archive. Pack SHA-256
`ebf8a1a860326ca0d0cee16fc02ff91c71ba06bf089672eee9e3671ec066713c`;
visual report SHA-256
`f5e71c32080244a6dc7bc46049be1dceabb1c6d65883851fe767a0646b4ef70f`;
rights report SHA-256
`c63f6dcaf647dd56973756f8b266e033e0c410586d2dfaf82a2ebcc60f828937`.

## Embedded compositions and high-resolution runtime fallback (2026-10-27)

1. **DONE** — Source-painted backplates containing contrasting detail are
   recognized generically and emitted as `complete-icon`/`embedded`; the rule
   neither fills arbitrary holes nor creates a generic tile.
2. **DONE** — 1Password, Aboard, ABUS Group, Acronis, AdGuard Home, and AdGuard
   Home Sync use their source compositions with transparent exterior pixels.
3. **DONE** — 2K and .ENV use explicit reviewed Simple Icons matte mappings,
   preserving brand paint, opaque white internal lettering, and transparent
   exterior pixels.
4. **DONE** — Format-3 raster fallbacks retain bounded final-size 128-pixel
   canonical RGBA, eliminating the prior 24-to-128 upscale for Aboard and
   Acronis while preserving strict source-to-final-PNG verification. SVG text
   stays on the deterministic canonical raster-SVG path because local font
   selection and hinting are not accepted as stable final-pixel inputs.
5. **DONE** — The OTP Harbor handoff assigns catalog-wide blank/covered and
   semantic pixel analysis to the builder. Consumer import remains bounded
   structural/hash validation plus lazy on-demand decode.
6. **DONE** — V1/v2 behavior and manifest contracts remain unchanged. This
   revision uses existing v3 `complete-icon`/`embedded` semantics and therefore
   requires no new consumer contract implementation.
7. **DONE** — Release build succeeds with zero warnings; all 164 tests pass.
   Two complete offline v3 builds, visual reports, and rights reports are
   byte-identical and reopen with zero unresolved failures.

Final evidence: 5,745 brands; 11,490 appearance references; 5,917 unique PNGs;
5,573 shared references; 18,993,513 PNG bytes; 30,951-byte largest PNG;
3,209.99-byte average PNG; 42,578,617-byte archive. Pack SHA-256
`928a67486135389bee475fcce325474ea1d4c4b50ed27372a5a4ad99bf079953`;
visual report SHA-256
`215c661985066421a11b8791b229fdfba48936e7970e81910d2567dfc74b3afe`;
rights report SHA-256
`1b9e6b65936560af187ae303825a1eda1440a2d011fa039fa54aaf0bc9544b2b`.

## Reviewed badge composition and safe inset (2026-10-26)

1. **DONE** — AdAway, AdBlock, and Adblock Plus use explicit reviewed
   provider/source composition mappings; authentic red paint and opaque white
   internal details are embedded while the exterior remains transparent.
2. **DONE** — Both theme appearances emit `complete-icon`/`embedded` and share
   identical content-addressed PNG bytes without consumer changes.
3. **DONE** — Format-3 artwork is fitted within a centered 112-pixel safe area,
   giving wide marks a deterministic eight-pixel margin; v1/v2 rendering is
   unchanged.
4. **DONE** — Synthetic tests cover manifest semantics, exterior transparency,
   internal opaque white, authentic paint, antialiasing, archive reopen/visual
   validation, deterministic reuse, and v1/v2 isolation.
5. **DONE** — The Simple Icons audit is non-mutating: only three confirmed
   mappings changed. Topology-only candidates remain pending source-backed
   review because closed holes are not evidence of an intended white fill.
6. **DONE** — Two full pinned-source v3 builds reopened successfully and are
   byte-identical, including visual and rights reports. The contract-aware
   light/dark contact sheet was manually inspected and the full visual report
   has zero unresolved failures.
7. **DONE** — The consumer-visible contract delta is explicitly documented in
   `docs/OTP-HARBOR-V3-BADGE-INSET-CONSUMER-PROMPT.md`. Project instructions
   now require an updated OTP Harbor transfer prompt and an explicit completion
   notice after every future consumer-contract change.

Final revised v3 evidence: 5,745 brands; 11,490 appearance references; 5,925
unique PNGs; 20,013,038 PNG bytes; 40,142-byte largest PNG; 3,377.73-byte
average PNG; 43,637,421-byte archive. Pack SHA-256
`7cf5dde78cfa5ed92bad7a9231d6fec88c6ea10115e3d7b8cffd2e8c7a90e078`.
The visual report SHA-256 is
`f58b75f3bec6a9740f6093ab8ac7988b007acbd917dd3279bfbe38e845948a29`;
the rights report SHA-256 is
`1b9e6b65936560af187ae303825a1eda1440a2d011fa039fa54aaf0bc9544b2b`.

## Theme-aware v3 revision (2026-10-26)

1. **DONE** — Separate strict v3 schema requires light/dark appearances, `foreground`/`complete-icon` roles, role-consistent backgrounds, appearance provenance, and content-addressed assets.
2. **DONE** — Dashboard acquisition cache v9 downloads every safe referenced light/dark SVG from the pinned revision through the existing bounded validation pipeline and cannot promote a variant-only/non-SVG metadata record to a brand.
3. **DONE** — Dashboard variants and recognized Aegis `bg.var`/`fg.var`/`fg.bg.var` assets are associated with primary records and remain noncanonical on their own; Aegis variations require a future reviewed mapping rather than automatic theme selection.
4. **DONE** — Simple Icons primary color is applied as verified canonical foreground paint; multicolor artwork and recognized complete compositions are preserved without inversion.
5. **DONE** — Explicit source overrides remain authoritative; otherwise the configured Dashboard-first provider policy is applied before deterministic quality ranking within that provider.
6. **DONE** — Both appearances are rendered, content-deduplicated, hashed, packaged, reopened, and visually checked; selection reasons are emitted per appearance.
7. **DONE** — V1/v2 schemas and archive layouts remain unchanged; v3 is now the CLI default and v1/v2 remain explicit.
8. **DONE** — 160 synthetic tests cover provider variants, self-references, Aegis implicit SVG fill in v2/v3, neutral dark-theme fallback, color semantics, reviewed badge composition, safe insets, archive reopen, complete icons, sharing, schema, strict reader semantics, deterministic output, and CLI behavior.
9. **DONE** — Product instructions, AGENTS, README, normative format docs, v3 schema, CLI help, and a v3 consumer handoff describe the contract.
10. **DONE** — Release build/test and real pinned-source v1/v2/v3 builds reopen successfully; two final v3 runs and their reports are byte-identical with 0/11,490 unresolved appearances and repository hygiene is clean.

Full v3 evidence: 5,745 brands; 11,490 appearance references; 5,925 unique
PNGs; 5,565 shared references; 21,722,265 PNG bytes; 42,600-byte largest
PNG; 3,666.21-byte average PNG; 45,346,709-byte archive. The visual report
contains zero unresolved failures. Two independently assembled archives are
byte-identical. Pack SHA-256
`e5183a25d0e05b56def6735b12bb6882ea7678606920720007946bccdbcc547e`.

Focused acceptance evidence is recorded in
`docs/FORMAT-V3-ACCEPTANCE-REVIEW.md`. A packaged-asset contact sheet confirmed
the OpenAI/Auth0 complete compositions, Dashboard native pair behavior, neutral
fallback tiles, and preserved multicolor artwork. Aegis remains enabled because
removing it would drop 459 brands without a Dashboard/Simple Icons replacement.
The rebuilt v2 compatibility artifact also promotes Aegis SVG-default black to
the manifest background before foreground rasterization, fixing OpenAI without
changing the frozen v1 SVG path.

# Format v2 implementation ledger

## PNG-only v2 revision (2026-10-20)

1. **DONE** — V2 brand manifests are PNG-only and reject `icon`, including null.
2. **DONE** — V2 packages contain only runtime PNGs and referenced licenses.
3. **DONE** — Full pre/post-change v1 packs and reports are byte-identical.
4. **DONE** — Completed v2 PNGs are reopened with strict profile, scanline-filter, hash, coverage, blank/covered, and size validation.
5. **DONE** — Packaged PNG bytes are compared with internal normalized SVG references before publication.
6. **DONE** — Malformed profile/filter/CRC/chunk/trailing/blank/path/schema/coverage/atomicity tests pass.
7. **DONE** — Product, format, handoff, README, CLI, AGENTS, and rights documentation describe PNG-only v2.
8. **DONE** — Release solution build and all 144 tests pass; both schemas parse and their contracts are asserted by tests.
9. **DONE** — Two complete real v2 builds are byte-identical, reopen successfully, and report zero unresolved failures.
10. **DONE** — Git hygiene, final diff, archive contents, size, hashes, coverage, and remaining risks were audited.

The table below records the earlier dual-asset implementation. Its v2 layout
was intentionally superseded before release by the PNG-only revision above;
the safe renderer, deterministic encoder, v1 compatibility, rights work, and
visual gates remain applicable.

| Task | Status | Acceptance evidence |
| --- | --- | --- |
| T1 — Define format version 2 | SUPERSEDED | The unreleased dual-asset layout was replaced by the strict PNG-only contract above. |
| T2 — Define raster profile | DONE | Normative `otp-harbor-foreground-v1` constants and format documentation. |
| T3 — Safe builder rendering | DONE | Static validation -> canonical normalization/validation -> bounded in-process render -> deterministic encode -> reopen -> visual compare, with cancellation and alternate selection. |
| T4 — Deterministic output | DONE | Deterministic fixed-Huffman PNG compression, v2 no-compression ZIP setting, stable hashes/order/timestamps, and repeated-byte tests/builds. |
| T5 — v1 compatibility and CLI | DONE | v1 remains explicitly selectable and has no runtime members; the default was later moved to v3 without changing frozen v1/v2 behavior. |
| T6 — Version-aware reader | DONE | Strict v1/v2 dispatch; exact PNG paths/profile/media/dimensions/filter/hash, archive budgets, and no extras. |
| T7 — Visual fidelity | DONE | Source-to-internal-canonical and internal-canonical-to-packaged-PNG 40x40/24x24 gates retain alpha, clipping, colors, separation, blank/covered checks. |
| T8 — Tests | DONE | 144 tests pass, including PNG-only runtime, security, CLI, atomicity, offline, deterministic, controlled end-to-end, and SVG cache-admission regression cases. |
| T9 — Documentation and handoff | DONE | Product instructions, README, format docs, schemas, rights policy, and consumer handoff updated. |
| T10 — Legal and provenance | DONE | Pinned authoritative provider documents reviewed; derivative/provenance rules and counsel-only questions documented. |

## Final verification checklist

- [x] Full solution builds (Release, zero warnings/errors).
- [x] Full test suite passes (144/144).
- [x] Controlled v1 and v2 packs build and reopen successfully.
- [x] v1 contains no runtime PNG files.
- [x] Every v2 brand has exactly one runtime PNG and no packaged SVG or `icon` member.
- [x] Repeated controlled builds are byte-identical.
- [x] Full controlled-source visual-fidelity gate reports zero unresolved failures.
- [x] Git status/diff inspected.
- [x] No upstream collection or generated third-party pack is tracked.
- [x] Remaining OTP Harbor consumer work is explicitly documented and reported.
