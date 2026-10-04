# OTP Harbor Icon Pack Builder

Builds a unified OTP Harbor icon pack from supported third-party icon
sources.

The builder is intended to combine and normalize icon metadata from
sources such as:

- Aegis Icons
- Simple Icons
- Dashboard Icons

The resulting OTP Harbor icon pack can contain:

- normalized brand identifiers
- issuer aliases
- SVG icons
- source metadata
- license information

## Goals

- Local/offline processing
- No network access required by OTP Harbor
- Deduplication across icon providers
- Canonical issuer mapping
- Reproducible icon-pack generation
