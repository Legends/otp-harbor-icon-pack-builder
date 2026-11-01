# 9router pack refresh for OTP Harbor

Work in the OTP Harbor application repository and read its AGENTS.md first.

Builder 3.0.1 renders the reviewed 9router Dashboard Icons source at its final
128-pixel runtime size. Its older
packaged PNG visibly enlarged 24-pixel lettering and showed scaling bands.
The source record, canonical brand ID `9router`, aliases, provenance, and
rights metadata are unchanged.

There is no manifest field, allowed value, render profile, runtime path
pattern, appearance role, or background semantic change. The 9router PNG has
new content and a new SHA-256 content-addressed path. Keep existing strict PNG
and archive validation. Existing cache revision behavior should use the new
asset SHA-256. No provider processing or source SVG rendering belongs in the
consumer.

Generate a new local pack with builder 3.0.1 and import it transactionally.
Check 9router at actual list size in light and dark themes; verify that the
white “9R” glyphs and black rounded tile are sharp, without horizontal
scaling bands. Reimporting the old pack cannot repair its already-encoded PNG.
No consumer code change is expected if format 3 is already implemented.

The builder release distributes only executables and documentation. The pack
contains third-party artwork and remains a user-side artifact subject to
source-specific licenses, trademark rights, and brand guidance. The builder
and pack are not endorsed by the depicted brands.
