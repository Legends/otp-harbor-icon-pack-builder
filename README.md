# OTP Harbor Icon Pack Builder

OTP Harbor Icon Pack Builder creates one local `.otphicons` file from tested Aegis Icons, Simple Icons, and Dashboard Icons sources pinned to each builder release. It downloads the source material only while you build the pack; no third-party icon collection is bundled with the executable or hosted in this repository.

## Download and run

Open the [latest release](https://github.com/Legends/otp-harbor-icon-pack-builder/releases/latest)
and choose the download for your computer. Starting with v1.2.0, the release
contains:

| Platform | Download |
| --- | --- |
| Windows on Intel or AMD | `OtpHarbor.IconPackBuilder-win-x64.exe` |
| Windows on ARM | `OtpHarbor.IconPackBuilder-win-arm64.exe` |
| macOS on Apple Silicon | `OtpHarbor.IconPackBuilder-osx-arm64.tar.gz` |
| macOS on Intel | `OtpHarbor.IconPackBuilder-osx-x64.tar.gz` |
| Linux on Intel or AMD | `OtpHarbor.IconPackBuilder-linux-x64.tar.gz` |
| Linux on ARM64 | `OtpHarbor.IconPackBuilder-linux-arm64.tar.gz` |

### Windows

1. Double-click the downloaded `.exe` file.
2. Press Enter to accept the proposed output location in your Downloads folder.
3. Wait for the green `[+] Icon pack created successfully.` message.
4. Press Enter to close the window.

### macOS

1. Double-click the downloaded `.tar.gz` file to extract it.
2. Open the extracted folder.
3. Double-click `Run OTP Harbor Icon Pack Builder.command`.
4. Press Enter to accept the proposed output location in your Downloads folder.
5. Wait for the green `[+] Icon pack created successfully.` message.

The macOS release is not currently notarized. If macOS blocks the first launch,
Control-click the launcher, choose **Open**, and confirm that you want to open
it. Do not disable Gatekeeper globally.

### Linux

1. Extract the downloaded `.tar.gz` file.
2. Open the extracted folder.
3. Run `run-otp-harbor-icon-pack-builder.sh`. When offered a choice by your
   file manager, choose **Run** or **Run as a program**.
4. Press Enter to accept the proposed output location in your Downloads folder.
5. Wait for the green `[+] Icon pack created successfully.` message.

Linux desktop behavior differs by distribution. The archive preserves the
launcher and executable permissions, but a file manager may still require you
to mark the launcher as trusted before it will run.

The finished file is normally:

```text
Windows: C:\Users\<you>\Downloads\otp-harbor-icons.otphicons
macOS:   /Users/<you>/Downloads/otp-harbor-icons.otphicons
Linux:   /home/<you>/Downloads/otp-harbor-icons.otphicons
```

All release executables are self-contained: you do not need to install .NET,
Visual Studio, Node.js, Python, or provider-specific tools. Internet access is
required when downloading or refreshing the upstream sources. The first build
can take several minutes while thousands of icons are downloaded, normalized,
and visually checked.

The executables are currently unsigned. Windows SmartScreen or platform trust
controls may therefore show a warning. Confirm that the download came from
this GitHub repository and compare its SHA-256 value with `SHA256SUMS.txt` from
the same release before opening it.

## What you will see

The builder asks before choosing a different output location and before replacing an existing pack. At both `[Y/n]` prompts, pressing Enter means **Yes**.

During catalog creation, an ASCII progress bar shows the percentage, completed and total brands, current brand, and elapsed time. A successful build ends in green, displays the generated pack location on a separate bright `[OUTPUT]` line, and offers to reveal the file in the platform file manager. An error ends in red and the window remains open so you can read the message.

Alongside the pack, the builder writes:

```text
otp-harbor-icons.visual-report.json
otp-harbor-icons.rights-report.json
```

The visual report confirms that the catalog passed the technical visual gate.
The rights report records the evidence available for each selected asset.
Neither technical validation nor an evidence status is legal clearance.

## Files and privacy

The builder contacts the official upstream GitHub repositories for:

- Aegis Icons;
- Simple Icons;
- Dashboard Icons.

Each builder release carries a tested compatibility set of exact upstream releases and commits. Release-archive downloads are checked against source-controlled SHA-256 values, and commit-based sources use a full commit ID. Normal builds use those pinned sources instead of following a moving `latest` release or Dashboard Icons `main` branch. Updating the builder updates the supported source set; `--refresh` only redownloads the versions pinned by the installed builder. Downloads are cached so later builds can reuse validated files, and offline mode accepts only this release's supported cache identity. The cache locations are:

```text
Windows: %LOCALAPPDATA%\OTP Harbor\IconPackBuilder\cache
macOS:   ~/Library/Caches/OTP Harbor/IconPackBuilder
Linux:   ${XDG_CACHE_HOME:-~/.cache}/otp-harbor/icon-pack-builder
```

No account data, issuer data, or generated pack is uploaded by this tool. The generated pack and reports remain on your computer.

## Command-line use

Most users can use the launch instructions above. PowerShell users can choose
an output path explicitly:

```powershell
.\OtpHarbor.IconPackBuilder-win-x64.exe build `
  --output "$env:USERPROFILE\Downloads\otp-harbor-icons.otphicons"
```

From a macOS or Linux terminal, run the executable inside the extracted folder:

```sh
./OtpHarbor.IconPackBuilder build \
  --output "$HOME/Downloads/otp-harbor-icons.otphicons"
```

For unattended use:

```powershell
.\OtpHarbor.IconPackBuilder-win-x64.exe build `
  --output "C:\Packs\otp-harbor-icons.otphicons" `
  --non-interactive `
  --force
```

Available options:

```text
--output <directory-or-file>
--offline
--refresh
--non-interactive
--force
--cache <directory>
--mappings <directory>
--provider-preference <comma-separated provider IDs>
--rights-policy <preserve|documented-only|require-documented>
--conflict-report <path.json>
--aegis <local.zip>
--simple-icons <local.zip>
--dashboard-icons <local.zip>
```

`--offline` disables network access and requires every source to exist in the validated cache or be supplied as a local override. `--refresh` redownloads this builder release's pinned source set. `--non-interactive` never prompts, and it will not replace an existing output unless `--force` is also supplied.

## How the catalog is created

The default rights policy is preserve: it keeps deterministic visual selection
and records the selected asset's evidence status. The documented-only policy
excludes source records without asset-level license evidence before selection.
The require-documented policy keeps normal selection but stops if a selected
asset lacks that evidence. These are evidence filters, not legal opinions:
metadata can be incomplete, a copyright license may not grant trademark rights,
and a documented license may still impose conditions.

The three upstream catalogs are merged into one deterministic set of canonical brands. Matching uses issuer aliases, not account names. Exact and explicitly mapped identities are preferred; ambiguous aliases are excluded instead of being assigned to the wrong brand.

Every selected SVG is converted to a restricted, safe representation and compared with its source at the target tile size. A pack is written only when the complete catalog passes this verification. The output contains exact upstream revisions, source hashes, selected-icon provenance, and the license, credit, disclaimer, README, or trademark documents recognized in the pinned source snapshots.

The `.otphicons` container format is documented in [Icon pack format](docs/ICON-PACK-FORMAT.md).

Release coverage and remaining portability work are tracked on the
[product agenda](docs/PRODUCT-AGENDA.md).

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

[Simple Icons expressly warns](https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md)
that its project-level CC0 dedication does not establish CC0 status for each
icon and that license data can be missing or outdated. [Aegis Icons documents
mixed upstream licensing](https://github.com/aegis-icons/aegis-icons) in its
README; Aegis assets therefore remain unknown unless a source-specific manual
assessment is added. [Dashboard Icons uses
Apache-2.0](https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE)
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
