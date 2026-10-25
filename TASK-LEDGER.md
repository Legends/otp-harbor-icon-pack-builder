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
| T5 — v1 compatibility and CLI | DONE | v1 remains the default and has no runtime members; `--format-version 1|2` plus v2 compatibility notice. |
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
