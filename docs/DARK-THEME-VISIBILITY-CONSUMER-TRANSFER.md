# Dark-theme visibility correction: consumer verification

Work in the OTP Harbor application repository. Read its AGENTS.md first.

The builder corrects v3 appearance selection against the account-card surfaces
`#FFFFFF` (light) and `#10213F` (dark). Transparent marks with insufficient
contrast retain their source colors on a manifest-declared solid tile.
Dashboard/Aegis SVG-default black is preserved, fixing Bazarr's previously
white-only circle. Runtime padding and 128x128 encoding are unchanged.

There are no new manifest fields, enum values, paths, render profiles, or
consumer rendering rules. The performance requirement is now the fastest
practical build with the best overall user experience, without a hard
one-minute deadline. That policy requires no consumer implementation change.

Import the corrected pack transactionally and verify on Android and desktop:

- Bazarr has its interior motif in both themes.
- Dark-theme wordmarks including Akamai, Appsmith, The Guardian, Bank of
  America, and Apache FreeMarker are legible with their declared backgrounds.
- Android has no consumer-added tile in its dark `complete-icon` appearance.
- `complete-icon`/`embedded` receives no extra background or padding;
  `foreground`/`solid` uses exactly the declared color and existing fitting.
- Theme switching selects the appropriate appearance without reimporting,
  recoloring, or discarding all decoded assets. Cache revisions remain tied to
  the asset SHA-256.

Keep all existing transactional archive, path, size, bounded PNG structure,
and hash validation. Catalog-wide PNG decoding, blank-output analysis, and
visual fidelity checks belong to the builder. Do not add an import-time
pixel-analysis pass or SVG/provider processing to the app. Decode requested
PNGs off the UI thread into the existing bounded bitmap cache.

No consumer code change is expected when the existing v3 contract is already
implemented. Report actual device/theme verification separately from builder
tests; the builder cannot certify a phone's installed pack or rendering state.
