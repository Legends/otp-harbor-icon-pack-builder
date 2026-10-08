# OTP Harbor Icon Pack Builder

OTP Harbor Icon Pack Builder creates one local `.otphicons` file from the latest supported Aegis Icons, Simple Icons, and Dashboard Icons sources. It downloads the source material only while you build the pack; no third-party icon collection is bundled with the executable or hosted in this repository.

## Download and run on Windows

1. Open the [latest release](https://github.com/Legends/otp-harbor-icon-pack-builder/releases/latest).
2. Download `OtpHarbor.IconPackBuilder-win-x64.exe`.
3. Double-click the downloaded file.
4. Press Enter to accept the proposed output location in your Downloads folder.
5. Wait for the green `[+] Icon pack created successfully.` message.
6. Press Enter to close the window.

The finished file is normally:

```text
C:\Users\<you>\Downloads\otp-harbor-icons.otphicons
```

The release executable is self-contained: you do not need to install .NET, Visual Studio, Node.js, Python, or provider-specific tools. Internet access is required when downloading or refreshing the upstream sources. The first build can take several minutes while thousands of icons are downloaded, normalized, and visually checked.

The executable is currently unsigned. Windows SmartScreen may therefore show an unrecognized-app warning. Confirm that the publisher location is this GitHub repository and compare the file's SHA-256 value with `SHA256SUMS.txt` from the same release before choosing **Run anyway**.

## What you will see

The builder asks before choosing a different output location and before replacing an existing pack. At both `[Y/n]` prompts, pressing Enter means **Yes**.

During catalog creation, an ASCII progress bar shows the percentage, completed and total brands, current brand, and elapsed time. A successful build ends in green; an error ends in red and the window remains open so you can read the message.

Alongside the pack, the builder writes:

```text
otp-harbor-icons.visual-report.json
```

This diagnostic report confirms that the complete catalog passed the visual verification gate. It is not part of the icon pack and does not need to be imported or copied elsewhere.

## Files and privacy

The builder contacts the official upstream GitHub repositories for:

- Aegis Icons;
- Simple Icons;
- Dashboard Icons.

Each build pins an exact upstream release or commit. Downloads are cached so later builds can reuse validated files. The cache location on Windows is:

```text
%LOCALAPPDATA%\OTP Harbor\IconPackBuilder\cache
```

No account data, issuer data, or generated pack is uploaded by this tool. The generated pack and reports remain on your computer.

## Command-line use

Most users can double-click the executable. PowerShell users can choose an output path explicitly:

```powershell
.\OtpHarbor.IconPackBuilder-win-x64.exe build `
  --output "$env:USERPROFILE\Downloads\otp-harbor-icons.otphicons"
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
--conflict-report <path.json>
--aegis <local.zip>
--simple-icons <local.zip>
--dashboard-icons <local.zip>
```

`--offline` disables network access and requires every source to exist in the validated cache or be supplied as a local override. `--non-interactive` never prompts, and it will not replace an existing output unless `--force` is also supplied.

## How the catalog is created

The three upstream catalogs are merged into one deterministic set of canonical brands. Matching uses issuer aliases, not account names. Exact and explicitly mapped identities are preferred; ambiguous aliases are excluded instead of being assigned to the wrong brand.

Every selected SVG is converted to a restricted, safe representation and compared with its source at the target tile size. A pack is written only when the complete catalog passes this verification. The output contains its exact upstream revisions, source hashes, selected-icon provenance, licenses, and attribution files.

The `.otphicons` container format is documented in [Icon pack format](docs/ICON-PACK-FORMAT.md).

## Legal and distribution notice

This repository and its releases do not redistribute upstream icon collections or generated icon packs. Icons, names, and trademarks remain the property of their respective rights holders. Review the included upstream licenses, trademark policies, and brand guidelines before using or sharing a generated pack.

Dashboard Icons identifies its repository license as [Apache License 2.0](https://github.com/homarr-labs/dashboard-icons/blob/main/LICENSE). The builder copies that license from the same immutable commit as the selected Dashboard Icons data and preserves it in the generated pack. Apache-2.0 does not grant permission to use third-party trademarks represented by individual icons; applicable trademark and brand-usage rules remain separate.

The generated pack's `pack.json` and `licenses/` entries are the authoritative record of the sources, licenses, attribution, and provenance included in that particular build.
