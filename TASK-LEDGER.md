# Format v2 implementation ledger

This ledger tracks the builder-side `.otphicons` format v2 work requested on
2026-10-20. Statuses are updated as implementation and verification progress.

| Task | Status | Acceptance evidence |
| --- | --- | --- |
| T1 — Define format version 2 | DONE | Frozen v1 schema plus separate strict v2 schema; required canonical SVG/runtime PNG descriptor and one-to-one coverage. |
| T2 — Define raster profile | DONE | Normative `otp-harbor-foreground-v1` constants and format documentation. |
| T3 — Safe builder rendering | DONE | Static validation -> canonical normalization/validation -> bounded in-process render -> deterministic encode -> reopen -> visual compare, with cancellation and alternate selection. |
| T4 — Deterministic output | DONE | Deterministic fixed-Huffman PNG compression, v2 no-compression ZIP setting, stable hashes/order/timestamps, and repeated-byte tests/builds. |
| T5 — v1 compatibility and CLI | DONE | v1 remains the default and has no runtime members; `--format-version 1|2` plus v2 compatibility notice. |
| T6 — Version-aware reader | DONE | Strict v1/v2 dispatch; exact paths/profile/media/dimensions, PNG/CRC/decode/hash/fidelity, archive budgets, and no extras. |
| T7 — Visual fidelity | DONE | Source-to-canonical gate retained; canonical-to-packaged-PNG 40x40/24x24 comparison added with alpha, clipping, colors, separation, blank/covered checks and zero unresolved controlled failures. |
| T8 — Tests | DONE | 134 tests pass, including focused runtime, security, CLI, atomicity, offline, deterministic, controlled end-to-end, and SVG cache-admission regression cases. |
| T9 — Documentation and handoff | DONE | Product instructions, README, format docs, schemas, rights policy, and consumer handoff updated. |
| T10 — Legal and provenance | DONE | Pinned authoritative provider documents reviewed; derivative/provenance rules and counsel-only questions documented. |

## Final verification checklist

- [x] Full solution builds (Release, zero warnings/errors).
- [x] Full test suite passes (134/134).
- [x] Controlled v1 and v2 packs build and reopen successfully.
- [x] v1 contains no runtime PNG files.
- [x] Every v2 brand has exactly one canonical SVG and one runtime PNG.
- [x] Repeated controlled builds are byte-identical.
- [x] Full controlled-source visual-fidelity gate reports zero unresolved failures.
- [x] Git status/diff inspected.
- [x] No upstream collection or generated third-party pack is tracked.
- [x] Remaining OTP Harbor consumer work is explicitly documented and reported.
