# OTP Harbor Icon Pack Builder

OTP Harbor Icon Pack Builder creates one local `.otphicons` pack from Aegis Icons, Simple Icons, and Dashboard Icons. The builder downloads tested, pinned upstream sources when you run it. The finished pack works offline, and this repository does not bundle or publish third-party icon collections.

## Download and run

Download the file for your computer from the [latest release](https://github.com/Legends/otp-harbor-icon-pack-builder/releases/latest). On Windows, open the `.exe`; on macOS or Linux, extract the archive and run the included launcher. Press Enter to save `otp-harbor-icons.otphicons` in Downloads. The release includes everything needed to run; the first build needs internet access and may take several minutes.

The executables are unsigned, and the macOS release is not notarized. Check the download against `SHA256SUMS.txt` from the same release before opening it. If macOS blocks the launcher, Control-click it and choose **Open**; do not disable Gatekeeper globally.

| Platform | Download |
| --- | --- |
| Windows on Intel or AMD | `OtpHarbor.IconPackBuilder-win-x64.exe` |
| Windows on ARM | `OtpHarbor.IconPackBuilder-win-arm64.exe` |
| macOS on Apple Silicon | `OtpHarbor.IconPackBuilder-osx-arm64.tar.gz` |
| macOS on Intel | `OtpHarbor.IconPackBuilder-osx-x64.tar.gz` |
| Linux on Intel or AMD | `OtpHarbor.IconPackBuilder-linux-x64.tar.gz` |
| Linux on ARM64 | `OtpHarbor.IconPackBuilder-linux-arm64.tar.gz` |

## Command-line usage

Use `build` to choose an output path. For example, on Windows:

```powershell
.\OtpHarbor.IconPackBuilder-win-x64.exe build --output "$env:USERPROFILE\Downloads\otp-harbor-icons.otphicons"
```

On macOS or Linux, use `./OtpHarbor.IconPackBuilder` in the extracted folder instead. Format 3 is the default. Use `--format-version 1` or `2` only for an older importer.

Use `--offline` for cached or locally supplied sources, `--refresh` to redownload this builder release's pinned sources, and `--non-interactive --force` for unattended replacement. Supply local source archives with `--aegis`, `--simple-icons`, or `--dashboard-icons`. The default rights policy, `preserve`, records evidence without filtering assets that lack it. Use `--rights-policy documented-only` to exclude those assets, or `require-documented` to stop if a selected asset lacks evidence. Run `--help` for all options.

The pack and its visual and rights reports stay on your computer. No account data, issuer data, or generated pack is uploaded. Available notices and provenance are stored in the pack; read the legal notice below before distributing or using third-party marks.

## Legal and distribution notice

This is a risk-control and provenance system, not automated legal clearance.
Available upstream notices and provenance are preserved where supplied, but
their inclusion does not prove that every asset is authorized for every
purpose, territory, modification, or distribution channel.

The evidence statuses have deliberately narrow meanings:

- unknown: no asset-level license evidence was found;
- documented: upstream supplied asset-level license metadata;
- attribution-required: the metadata identifies a non-public-domain license
  whose attribution, notice, or other conditions require review;
- restricted: a manual review identified a restriction requiring exclusion
  or individual review.

Documented never means legally cleared. Manual conclusions belong in
mappings/rights-assessments.json with an evidence URL and note. Repository
licenses are not automatically treated as licenses for every depicted logo.

[Simple Icons expressly warns](https://github.com/simple-icons/simple-icons/blob/dde88ab37611285a2bf1a7883c62be7e479794ba/DISCLAIMER.md)
that its project-level CC0 dedication does not establish CC0 status for each
icon and that license data can be missing or outdated. [Aegis Icons documents
mixed upstream licensing](https://github.com/aegis-icons/aegis-icons/tree/ec6dae28f1fa87688d691f2c43265083a1797b3c) in its
README; Aegis assets therefore remain unknown unless a source-specific manual
assessment is added. [Dashboard Icons uses
Apache-2.0](https://github.com/homarr-labs/dashboard-icons/blob/57e939e504eda0ea764098015da93aa666ad6f31/LICENSE)
for its repository and separately disclaims ownership or endorsement of brand
marks. Apache-2.0 itself does not grant trademark rights.

For EU trademarks, Article 14 of Regulation (EU) 2017/1001 permits some
identifying or referential use, but only in accordance with honest practices
in industrial or commercial matters. German MarkenG section 23 follows the
same structure. This is context-specific, not blanket permission. Copyright
remains independent: Article 2 of Directive 2001/29/EC reserves reproduction,
while Article 5 permits defined national exceptions. German UrhG section 53,
for example, limits private copying to specified circumstances and does not
authorize redistribution. Obtain qualified advice for a concrete commercial,
public, or cross-border use.

This repository and its releases do not redistribute upstream icon collections
or generated icon packs.

The generated pack's `pack.json` and `licenses/` entries record the sources,
evidence, notices, and provenance actually preserved in that build; they do
not guarantee completeness or permission.

The classification rules and primary legal sources are documented in the
[rights-evidence policy](docs/RIGHTS-POLICY.md).
The pinned provider review and unresolved legal questions are documented in
[pinned-provider rights and provenance review](docs/PROVIDER-RIGHTS-REVIEW.md).

Rights holders or their representatives can open a GitHub issue identifying
the provider, source ID, evidence, and requested action. Maintainers should
record the review in mappings/rights-assessments.json, exclude a disputed
asset when appropriate, and publish a new release rather than silently
replacing an existing release.

## Builder software license

The builder source code and documentation are licensed under
[Apache License 2.0](LICENSE). That license does not cover third-party icons,
logos, names, or trademarks. Runtime dependency notices are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), and distribution notices are
in [NOTICE](NOTICE).

Contributions use the same inbound Apache-2.0 terms described in
[CONTRIBUTING.md](CONTRIBUTING.md).
