# Bundles

## Purpose

The bundles capability manages metapackage definitions that aggregate multiple constituent NuGet packages into a single distributable unit, supporting independent or lockstep versioning strategies, constituent visibility rules, and composition validation.

> **Architecture Note — Relationship to Versioning:** The bundles capability defines the bundle data model, composition rules, and validation logic. When `versionStrategy: independent` is configured, the cascading bump algorithm (see spec `versioning`, requirement VN-07) evaluates the bundle's own public API surface to determine whether constituent bumps propagate to the bundle version. The CLI commands for bundle management (`titi bundle create|check|update|lint`) are defined in spec `cli` (CLI-12 through CLI-15).

## Requirements

### Requirement BN-01: Bundle Definition Model

The system SHALL support bundle (metapackage) definitions via `bundles.yaml` at the repo root. Each bundle entry specifies a name, a list of constituent package IDs, a `versionStrategy` (`independent` | `lockstep`, default: `lockstep`), and constituent reference attributes.

#### Scenario: Bundle creation
- **WHEN** `titi bundle create <name>` is invoked with a list of constituent packages
- **THEN** an entry is added to `bundles.yaml` with `PrivateAssets="none"` for each constituent and `IncludeBuildOutput=false` on the bundle project

#### Scenario: Bundle lint detects misconfiguration
- **GIVEN** a bundle whose constituent is missing `PrivateAssets="none"`
- **WHEN** `titi bundle lint` runs
- **THEN** exit code is 1 and the missing attribute is reported with the constituent package name

#### Scenario: Bundle independent versioning
- **GIVEN** a bundle with `versionStrategy: independent` in `bundles.yaml`
- **WHEN** the cascading bump runs (see `versioning` spec, VN-07)
- **THEN** the bundle's version is bumped only if a constituent's public API surface changed (as detected by ApiCompat on the bundle's own public API); internal-only bumps within constituents do not cascade to the bundle. The bundle has its own changeset files to control explicit version bumps independent of constituent changes.
