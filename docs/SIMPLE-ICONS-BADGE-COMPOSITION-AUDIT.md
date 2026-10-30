# Simple Icons badge-composition audit

This audit records the review boundary for format-3 badge composition. It does
not authorize automatic conversion based only on image topology.

## Confirmed mappings

The following source records have reviewed, source-backed badge semantics and
are listed explicitly in `mappings/appearance-compositions.json`:

| Canonical brand | Provider/source | Brand paint | Interior matte | Result |
| --- | --- | --- | --- | --- |
| AdAway | `simple-icons/adaway` | Simple Icons primary red | white | transparent exterior, complete icon |
| AdBlock | `simple-icons/adblock` | Simple Icons primary red | white | transparent exterior, complete icon |
| Adblock Plus | `simple-icons/adblockplus` | Simple Icons primary red | white | transparent exterior, complete icon |
| 2K | `simple-icons/2k` | Simple Icons primary red | white | transparent exterior, complete icon |
| .ENV | `simple-icons/dotenv` | Simple Icons primary yellow | white | transparent exterior, complete icon |

For both application themes, each result is emitted as `complete-icon` with an
`embedded` background. The builder fills only transparent regions enclosed by
the reviewed badge geometry. Boundary-connected transparency, including
antialiased exterior edges, remains transparent.

## Catalog audit

A non-mutating topology scan of the currently selected Simple Icons foreground
assets found 1,039 assets containing both boundary-connected transparency and
one or more enclosed transparent regions. This is a candidate set, not a list
of 1,039 defects: letters and many ordinary logos deliberately contain holes
that must remain transparent. Even stricter shape thresholds still produced
hundreds of false positives.

Consequently, topology is insufficient evidence for an opaque interior color.
Only the five confirmed records above are changed by reviewed matte
composition. Every
additional candidate requires one of:

- a source-provided complete colored asset;
- authoritative brand guidance defining the composition; or
- a reviewed canonical-brand override recording the provider, source ID,
  matte color, and rationale.

The audit intentionally does not infer interior white from a closed contour,
does not recolor multicolor logos, and does not mutate the source collection.
This prevents transparent counters in wordmarks and symbols from being filled
incorrectly.

## Review procedure

For a future candidate, compare the source evidence and the generated PNG on
both light and dark application surfaces. Confirm authentic paint, opaque
interior details, transparent exterior pixels, antialiased boundary behavior,
and `complete-icon`/`embedded` manifest semantics. Only then add an explicit
mapping and a synthetic regression fixture.
