# Pinned-provider rights and provenance review

Review date: 20 October 2026. This automated review is not legal advice and
does not determine whether a particular use is lawful in a particular
jurisdiction. Qualified legal counsel should review public, commercial,
cross-border, disputed, or otherwise material uses.

## Scope and result

The review covers the exact upstream revisions pinned in
`UpstreamDefinitions.cs`. It checks the providers' own license, attribution,
trademark disclaimer, redistribution language, and usage guidance. The v2
runtime PNG is a deterministic user-side derivative of the selected canonical
SVG. It remains tied to that SVG's provider/source ID, source revision, rights
evidence, guidelines, preserved notices, and limitations. Rasterization grants
no new copyright, trademark, affiliation, sponsorship, endorsement, or
redistribution right.

No generated combined pack or upstream icon collection may be committed to
this repository or published as a repository release.

## Aegis Icons

- Pin: release `2026-07-01`, commit
  `ec6dae28f1fa87688d691f2c43265083a1797b3c`.
- The project describes itself as an unofficial icon collection and says it is
  not associated with the Aegis Authenticator creators.
- Its repository license is CC0-1.0, but its README narrows that statement:
  CC0 applies to templates, text, code, and only some icons. Other artwork is
  credited under mixed licenses or remains property of trademark owners.
- Its trademark disclaimer says inclusion does not imply endorsement and asks
  public users to establish their right to use the relevant marks.
- Builder treatment: preserve the pinned README, LICENSE, credits available in
  the source, selected-source identity, and evidence status. Do not label the
  collection or its icons official. Unknown asset-level rights stay unknown.

Pinned evidence:

- <https://github.com/aegis-icons/aegis-icons/tree/ec6dae28f1fa87688d691f2c43265083a1797b3c>
- <https://github.com/aegis-icons/aegis-icons/blob/ec6dae28f1fa87688d691f2c43265083a1797b3c/README.md>
- <https://github.com/aegis-icons/aegis-icons/blob/ec6dae28f1fa87688d691f2c43265083a1797b3c/LICENSE.md>

## Simple Icons

- Pin: version `16.34.0`, commit
  `dde88ab37611285a2bf1a7883c62be7e479794ba`.
- The repository is distributed under CC0-1.0, whose limitations do not waive
  trademark or patent rights and do not clear third-party rights.
- The provider's disclaimer expressly says repository CC0 does not imply every
  icon is CC0; per-icon license data may be absent or outdated. It directs users
  to per-icon source, license, and brand-guideline records and recommends
  checking them for the intended use.
- Builder treatment: preserve provider metadata including title, source,
  guidelines, and per-icon license data when present, plus the pinned license
  and disclaimer. Missing metadata is not inferred as permission.

Pinned evidence:

- <https://github.com/simple-icons/simple-icons/tree/dde88ab37611285a2bf1a7883c62be7e479794ba>
- <https://github.com/simple-icons/simple-icons/blob/dde88ab37611285a2bf1a7883c62be7e479794ba/DISCLAIMER.md>
- <https://github.com/simple-icons/simple-icons/blob/dde88ab37611285a2bf1a7883c62be7e479794ba/LICENSE.md>

## Dashboard Icons

- Pin: commit `57e939e504eda0ea764098015da93aa666ad6f31`.
- The repository LICENSE is Apache-2.0. Redistribution conditions include
  providing the license, marking modified files where applicable, retaining
  pertinent notices, and preserving NOTICE attribution when supplied.
- Apache-2.0 section 6 does not grant trademark permission beyond reasonable
  origin description and NOTICE reproduction.
- The provider README separately says product names and trademarks belong to
  their owners, icons are used for identification, and inclusion does not imply
  endorsement.
- Builder treatment: acquire LICENSE and README from the same immutable commit,
  preserve `Apache-2.0` and the revision-pinned license URL in source metadata,
  and do not treat the repository license as conclusive asset-level trademark
  or third-party-logo permission.

Pinned evidence:

- <https://github.com/homarr-labs/dashboard-icons/tree/57e939e504eda0ea764098015da93aa666ad6f31>
- <https://github.com/homarr-labs/dashboard-icons/blob/57e939e504eda0ea764098015da93aa666ad6f31/README.md>
- <https://github.com/homarr-labs/dashboard-icons/blob/57e939e504eda0ea764098015da93aa666ad6f31/LICENSE>

## Unresolved legal questions

The builder cannot determine ownership, subsistence, territorial scope,
fair/referential-use defenses, consent, contractual brand-guideline effect,
required attribution placement, or whether an upstream contributor had all
necessary rights. It also cannot determine whether a raster derivative triggers
license-specific modification or change-marking duties for a concrete
distribution. These are jurisdiction- and use-specific questions for qualified
legal counsel, not automated build decisions.
