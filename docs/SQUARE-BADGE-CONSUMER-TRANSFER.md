# Square badge frames: consumer verification

Work in the OTP Harbor application repository and read its AGENTS.md first.
The builder repository has not modified consumer code.

The format-3 builder now composes these four brands as square badges with
opaque white interior details and a thin square white frame:

| Brand | Canonical ID | Exact source | Retained paint |
| --- | --- | --- | --- |
| 1&1 | `1-1` | `simple-icons/1and1` | blue `#003D8F` |
| AI Dungeon | `ai-dungeon` | `simple-icons/aidungeon` | black `#000000` |
| Booking.com | `booking-com` | `simple-icons/bookingdotcom` | blue `#003A9A` |
| The Weather Channel | `the-weather-channel` | `simple-icons/theweatherchannel` | blue `#003399` |

Source colors are preserved. For 1&1 and AI Dungeon, the former dark-theme
`foreground`/`solid` white tile is now included in the complete PNG. Render
the new `complete-icon`/`embedded` descriptor without adding another tile.
The frame is explicitly reviewed builder presentation, not official brand art.

Consumer-visible raster change: on the existing 128×128 canvas, the source
square occupies pixels 12–115 on each axis. A four-pixel white frame extends
the complete badge to pixels 8–119. The outer eight-pixel transparent inset
remains. Corners are sharp. Both themes reference identical content-addressed
PNG bytes, using the existing `complete-icon` role, `embedded` background, and
`reviewed-badge-composition` selection value.

There are no new manifest fields, allowed values, runtime paths, or render
profiles. `square-badge-matte-frame` is a builder mapping mode and is not a
consumer enum. No consumer fitting or implementation change is expected.

Import the rebuilt pack transactionally. On Android and desktop, verify all four
brands in light (`#FFFFFF`) and dark (`#10213F`) themes at actual list size:

- Preserve the source blue/black paint, white details, square corners, and thin frame.
- Render `complete-icon`/`embedded` as supplied, without adding a tile, padding,
  frame, recoloring, or rounded clipping. The white frame naturally blends
  into a white light-theme surface.
- Retain the existing asset-SHA-256 cache identity and theme switching behavior.
- Preserve all existing archive, path, size, bounded PNG, hash, and transactional
  validation. Pixel analysis and provider processing remain builder work.

Report device acceptance separately from builder preview verification. The
builder preview cannot establish a device's installed pack or rendering state.
