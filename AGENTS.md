# AGENTS.md

## Project purpose

OTP Harbor Icon Pack Builder creates a single normalized icon pack for OTP Harbor from user-supplied third-party icon sources.

The initial supported sources are:

- Aegis Icons
- Simple Icons
- Dashboard Icons

The builder must merge these sources into one deterministic catalog that OTP Harbor can consume locally without network access.

The important product concept is not "three icon packs in OTP Harbor". The output is one canonical OTP Harbor icon pack with:

- one canonical brand identity per service/brand,
- one selected icon per canonical brand,
- merged issuer aliases/synonyms,
- provenance for the selected icon and source data,
- licensing/attribution metadata,
- deterministic conflict handling.

OTP Harbor resolves icons from the mandatory TOTP `issuer` value. The optional account name is not part of automatic brand matching.

## Existing OTP Harbor repository

The existing OTP Harbor application repository is available locally at:

```text
E:\Repos\TOTP-Manager
```

Treat it as the authoritative integration reference. Before finalizing provider compatibility or the generated pack contract, inspect the relevant account/domain models, mandatory issuer handling, icon import and resolution code, JSON conventions, naming conventions, and tests. Important files include:

- `TOTP.Core/Models/Account.cs`
- `TOTP.Infrastructure/Parser/OtpAuthSupportPolicy.cs`
- `TOTP.Infrastructure/Services/QrPayloadValidator.cs`
- `TOTP.Infrastructure/Icons/SimpleIconsImporter.cs`
- `TOTP.Infrastructure/Icons/AegisIconPackImporter.cs`
- `TOTP.Infrastructure/Icons/IconImportArchive.cs`
- `TOTP.Infrastructure/Branding/IssuerAliasResolver.cs`
- `TOTP.Infrastructure/Services/SimpleIconsBrandIconPackService.cs`
- `TOTP.Tests/Services/IconPackImporterTests.cs`
- `TOTP.Tests/Services/IssuerAliasResolverTests.cs`
- `TOTP.Tests/Services/SimpleIconsBrandIconPackServiceTests.cs`

Keep this builder independent of the application assembly: reuse compatible parsing and security behavior, but do not add a project reference across repositories. Do not modify the OTP Harbor application repository unless the task explicitly requests it.

## Core constraints

1. **OTP Harbor remains offline/local-first.**
   - Do not add runtime network requirements to OTP Harbor.
   - This repository builds packs outside the OTP Harbor application.

2. **Do not redistribute third-party icon collections from this repository.**
   - Do not commit generated packs containing upstream icons.
   - Do not publish generated third-party icon bundles as GitHub Releases.
   - Do not commit downloaded upstream archives or extracted icon trees.
   - Generated packs are user-side build artifacts.
   - Preserve upstream license, attribution, trademark, and provenance information in generated output.

3. **Canonical identity belongs to OTP Harbor.**
   - Never use a provider slug as the permanent identity merely because it already exists.
   - Provider names/slugs are inputs.
   - OTP Harbor canonical IDs must be stable and provider-independent.

4. **Issuer matching must be deterministic and conservative.**
   - Exact and normalized alias matching is preferred.
   - Never silently map one alias to multiple canonical brands.
   - Ambiguities must be reported during the build.
   - A missing match is preferable to a wrong brand icon.

5. **The optional TOTP account name must not drive brand resolution.**
   - Matching is based on the mandatory issuer.
   - Account names may contain e-mail addresses, usernames, IDs, or customer numbers and are intentionally excluded from automatic brand matching.

6. **Builds must be reproducible.**
   - Stable ordering.
   - Stable canonical IDs.
   - Stable normalization.
   - Same inputs and mapping files should produce byte-equivalent metadata where practical.
   - Avoid timestamps inside content that is intended to be deterministic unless explicitly required.

## Technology

Use the .NET stack and C#.

Prefer:

- modern SDK-style projects,
- nullable reference types enabled,
- implicit usings enabled,
- async APIs where I/O is involved,
- System.Text.Json,
- built-in BCL functionality before third-party dependencies,
- xUnit for automated tests unless the repository already establishes another test framework.

Keep the CLI cross-platform even though primary development is on Windows.

Do not introduce UI frameworks. This is a command-line/build tool.

## Recommended solution structure

Keep the architecture clean without creating unnecessary projects.

A good initial structure is:

```text
OtpHarbor.IconPackBuilder.sln

src/
  OtpHarbor.IconPackBuilder/
    Program.cs
    Commands/
    Domain/
    Providers/
      Aegis/
      DashboardIcons/
      SimpleIcons/
    Normalization/
    Resolution/
    Packaging/

tests/
  OtpHarbor.IconPackBuilder.Tests/

schemas/
  otp-harbor-icon-pack.schema.json

mappings/
  canonical-brands.json
  issuer-aliases.json
  source-overrides.json
```

If a simpler structure provides the same separation, prefer the simpler structure.

## Provider architecture

Provider-specific parsing must be isolated behind a common abstraction.

Conceptually:

```csharp
public interface IIconSourceProvider
{
    string Id { get; }

    Task<ProviderCatalog> LoadAsync(
        IconSourceInput input,
        CancellationToken cancellationToken);
}
```

Each provider adapter is responsible only for understanding its upstream format.

Provider adapters must not decide OTP Harbor canonical IDs globally.

The merge/resolution layer owns canonicalization, alias merging, duplicate detection, source selection, and conflict reporting.

## Initial provider expectations

### Aegis Icons

Use the Aegis pack metadata when available, especially:

- icon name,
- issuer values,
- primary icon path,
- pack/version information.

Prefer primary icons over variation/generic assets unless an explicit mapping says otherwise.

Aegis issuer values are valuable alias candidates because the collection is curated around authenticator/2FA usage.

### Simple Icons

Use upstream metadata such as:

- title,
- slug,
- aliases,
- source URL,
- guidelines URL,
- license metadata,
- brand color when available.

Do not assume the Simple Icons slug should become the OTP Harbor canonical ID.

### Dashboard Icons

Use upstream metadata such as:

- slug,
- aliases,
- available SVG variants,
- source/provenance fields where available.

Prefer a normal/default SVG over light/dark variants unless an override explicitly chooses a variant.

Do not assume the Dashboard Icons website catalog is identical to the native icon files in the repository. Treat only the user-supplied source data as input.

## Canonical brand model

The exact model can evolve, but each canonical brand should support at least:

```json
{
  "id": "amazon-web-services",
  "displayName": "Amazon Web Services",
  "icon": "icons/amazon-web-services.svg",
  "issuerAliases": [
    "Amazon Web Services",
    "Amazon AWS",
    "AWS"
  ],
  "selectedSource": {
    "provider": "simple-icons",
    "sourceId": "amazonwebservices"
  }
}
```

Also retain pack-level source metadata so a generated pack can state which upstream inputs were used.

## Alias and issuer normalization

Create one well-tested normalization pipeline.

Normalization should be suitable for lookup keys, not for display text.

At minimum consider:

- trimming,
- Unicode normalization,
- case folding,
- collapsing whitespace,
- punctuation normalization/removal where safe,
- normalization of common separators.

Do not aggressively normalize distinct brands into the same key.

Keep the original alias strings for display/debug/provenance even when normalized keys are used for lookup.

The generated catalog should support fast exact lookup of a normalized issuer.

## Canonicalization strategy

Canonical identity resolution should happen in this order:

1. explicit manual canonical mapping,
2. existing canonical brand mapping,
3. safe deterministic merge based on normalized names/aliases,
4. otherwise create a distinct canonical brand.

Never merge two records merely because they are fuzzy-similar.

Examples that may legitimately represent the same brand:

- `AWS`
- `Amazon AWS`
- `Amazon Web Services`
- provider slug `amazonwebservices`

Examples that must remain distinguishable unless explicitly mapped:

- Amazon
- Amazon Web Services
- Microsoft
- Microsoft 365
- GitHub
- GitHub Actions

Manual mapping files are the authoritative place for exceptional cases.

## Icon source selection

The builder merges metadata from every matching provider record but selects one icon for the canonical brand.

Selection must be deterministic.

Use this order:

1. explicit entry in `mappings/source-overrides.json`,
2. configured provider preference,
3. provider-specific quality rules,
4. deterministic fallback.

The initial default provider preference may be:

```text
Aegis primary
Dashboard Icons default SVG
Simple Icons SVG
```

This is a default only. Do not encode it so deeply that it cannot be changed.

The selected icon source and original upstream identifier must be retained in pack metadata.

## Mapping files

Treat mapping files as source-controlled product data.

### mappings/canonical-brands.json

Use for explicit equivalence between provider identities and OTP Harbor canonical IDs.

### mappings/issuer-aliases.json

Use for aliases that cannot be safely inferred from upstream metadata.

### mappings/source-overrides.json

Use when a specific brand should use an icon from a specific provider/source identifier.

All mapping files must have deterministic schemas and tests.

## Output pack

Use a documented, versioned pack format.

Recommended extension:

```text
.otphicons
```

It may internally be a ZIP archive.

Recommended contents:

```text
pack.json
icons/
  amazon.svg
  amazon-web-services.svg
  github.svg
licenses/
  ...
```

The output schema must have an explicit schema/format version.

OTP Harbor should not need to understand Aegis, Simple Icons, or Dashboard Icons once it receives the generated pack.

## Security requirements

All imported archives/files are untrusted input.

Protect against:

- ZIP path traversal / Zip Slip,
- absolute paths,
- `..` traversal,
- excessive entry counts,
- excessive compressed/uncompressed sizes,
- decompression bombs,
- duplicate conflicting paths,
- malformed JSON,
- malformed or unexpectedly large SVG files,
- unsupported file types.

Do not execute scripts or binaries contained in source packs.

SVGs are data. Do not render them in the builder.

Use configurable but conservative limits.

## Diagnostics

Errors should identify:

- provider,
- source archive/path,
- upstream record/icon when known,
- reason for rejection,
- canonical brand/alias involved when relevant.

Produce a build summary containing at least:

- number of source records read per provider,
- canonical brand count,
- aliases count,
- duplicate/merge count,
- ambiguity/conflict count,
- number of selected icons by provider,
- skipped/invalid records.

Add an optional machine-readable conflict report.

## CLI behavior

The first version should support local source inputs.

A target UX may look like:

```powershell
OtpHarbor.IconPackBuilder build `
  --aegis .\sources\aegis-icons.zip `
  --simple-icons .\sources\simple-icons.zip `
  --dashboard-icons .\sources\dashboard-icons.zip `
  --output .\out\otp-harbor-icons.otphicons
```

Do not require all providers. The builder should work with any supported subset.

Do not add automatic downloading unless explicitly requested later.

## Testing expectations

Tests are mandatory for:

- provider parsing,
- issuer normalization,
- canonical brand merging,
- alias deduplication,
- alias collision detection,
- source override behavior,
- deterministic source selection,
- deterministic output ordering,
- invalid/malformed metadata,
- unsafe archive paths,
- archive size/count limits,
- pack schema serialization/deserialization.

Use small synthetic fixtures created specifically for tests.

Do not commit entire third-party icon packs as test data.

## Development behavior for Codex

When implementing a request:

1. inspect the existing repository before changing architecture,
2. reuse existing abstractions rather than duplicating them,
3. make the smallest coherent change that satisfies the requirement,
4. add/update tests with production changes,
5. run build and tests before reporting completion,
6. mention any assumptions that remain,
7. do not silently weaken validation to make a test pass.

If a requested approach has a clearly safer or simpler implementation, implement the better approach and explain the deviation briefly.

## Definition of done

A change is complete only when:

- the solution builds,
- automated tests pass,
- generated schemas/mappings are valid,
- no third-party icon collection is accidentally committed,
- documentation reflects externally visible behavior,
- errors are actionable,
- the result remains compatible with the local/offline OTP Harbor design.


## Related OTP Harbor repository

The existing OTP Harbor application repository is available locally at:

```text
E:\Repos\TOTP-Manager
