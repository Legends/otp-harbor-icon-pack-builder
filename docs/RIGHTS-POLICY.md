# Rights-evidence policy

Policy reviewed: 8 October 2026

This document explains the builder's automated evidence classifications. It is
not a legal opinion, does not create a lawyer-client relationship, and does not
clear any icon for a particular use.

## Legal framework reflected by the policy

International copyright protection starts from exclusive rights rather than a
general logo exception. Article 9 of the Berne Convention recognizes an
exclusive reproduction right and permits national exceptions only within its
defined framework. The WIPO Copyright Treaty confirms that this reproduction
right applies in the digital environment. Whether a particular logo meets the
threshold for copyright protection and whether an exception applies remain
questions of applicable law and facts.

Within the European Union:

- Article 2 of Directive 2001/29/EC reserves reproduction rights.
- Article 5 lists possible exceptions and limitations; it does not turn every
  personal or technical copy into an EU-wide unrestricted use.
- Article 14 of Regulation (EU) 2017/1001 limits EU trade-mark rights for some
  identifying or referential uses, subject to honest practices in industrial
  or commercial matters.

German law implements the same trade-mark structure in MarkenG section 23.
UrhG section 53 permits specified private copies by a natural person for
private, non-commercial use, subject to its conditions, and subsection 6
restricts distribution and public communication of those copies.

Primary sources:

- [Berne Convention, Article 9](https://www.wipo.int/wipolex/en/treaties/textdetails/12214)
- [WIPO Copyright Treaty agreed statements](https://www.wipo.int/wipolex/en/text/381455)
- [Regulation (EU) 2017/1001, Article 14](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:02017R1001-20251201)
- [Directive 2001/29/EC, Articles 2 and 5](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:02001L0029-20190606)
- [German MarkenG section 23](https://www.gesetze-im-internet.de/markeng/__23.html)
- [German UrhG section 53](https://www.gesetze-im-internet.de/urhg/__53.html)

Trademark rights, unfair-competition rules, copyright, moral rights, design
rights, contract terms, and brand guidelines can apply independently. WIPO
trademark treaties do not create one worldwide referential-use safe harbor, so
the builder does not label assets globally lawful.

## Evidence statuses

The assessment is attached to the selected-source metadata and written to the
sibling rights report.

| Status | Meaning | Automated effect |
|---|---|---|
| unknown | No asset-level license evidence was found. | Preserved by the default policy; excluded by documented-only; rejected by require-documented if selected. |
| documented | Asset-level upstream license metadata exists. | Eligible for documented-only and require-documented. |
| attribution-required | Asset-level metadata identifies a non-public-domain license whose attribution, notice, or other conditions require review. | Eligible, but the report retains the license and evidence needed for review. |
| restricted | A manual review recorded a restriction. | Not treated as documented and must receive individual review. |

Documented is not synonymous with authorized. A license record may be stale,
granted by a party lacking all relevant rights, limited to copyright, or
subject to conditions the builder cannot evaluate.

## Provider treatment

Simple Icons is assessed per icon from its license metadata. Its own disclaimer
states that project-level CC0 does not establish the status of each icon and
that metadata can be incomplete.

Aegis Icons describes mixed upstream licensing in its README. Because its pack
does not provide a reliable license field for each asset, its assets remain
unknown unless a manual, source-specific assessment is added.

Dashboard Icons publishes its repository under Apache-2.0 and includes a
trademark disclaimer. The repository license is preserved, but it is not
automatically assigned to every third-party logo. A Dashboard icon is
documented only when its own metadata carries a license or a manual assessment
supplies evidence.

## Manual review

Manual findings belong in mappings/rights-assessments.json and are keyed by
provider/sourceId. A record must identify a narrow status and should retain:

- the stated license type;
- an immutable or authoritative HTTPS evidence URL;
- a concise note describing what was reviewed.

Do not mark an asset documented merely because it appears in a repository with
a project-wide license. Review creator credits, asset-level metadata, license
scope, attribution and modification conditions, trademark guidance, and the
intended use. CC BY 4.0, for example, requires attribution and change marking
when material is shared, while expressly not licensing trademark rights.

## Disputes and release handling

A rights holder or representative may report a provider/source ID and
supporting evidence through the repository issue tracker. Maintainers should
preserve the report, assess exclusion promptly, record the result in the manual
mapping, and publish a new version. Existing release assets should not be
silently replaced because their hashes and source revision form part of the
audit trail.
