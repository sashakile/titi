# Bundles

## Purpose

The bundles capability manages metapackage definitions that aggregate multiple constituent NuGet packages into a single distributable unit, supporting independent or lockstep versioning strategies, constituent visibility rules, and composition validation.

> **Architecture Note — Relationship to Versioning:** The bundles capability defines the bundle data model, composition rules, and validation logic. Because a metapackage sets `IncludeBuildOutput=false`, it does not produce an assembly for ApiCompat comparison. When `versionStrategy: independent` is configured, bundle-version propagation is therefore based on changes to the bundle's externally visible dependency contract (constituent membership and constituent version floors recorded in the metapackage), not on assembly-level API comparison. The CLI commands for bundle management (`titi bundle create|check|update|lint`) are defined in spec `cli` (CLI-12 through CLI-15).

## Requirements

### Requirement BN-01: Bundle Definition Model

The system SHALL support bundle (metapackage) definitions via `bundles.yaml` at the repo root. Each bundle entry specifies a name, a list of constituent package IDs, a `versionStrategy` (`independent` | `lockstep`, default: `lockstep`), and the following metapackage attributes: `PrivateAssets="none"` on each constituent `PackageReference` (ensuring transitive flow-through) and `IncludeBuildOutput=false` on the bundle `.csproj` (since metapackages produce no assembly).

#### Scenario: Bundle creation
- **WHEN** `titi bundle create <name>` is invoked with a list of constituent packages
- **THEN** an entry is added to `bundles.yaml` with `PrivateAssets="none"` for each constituent and `IncludeBuildOutput=false` on the bundle project

#### Scenario: Bundle lint detects misconfiguration
- **GIVEN** a bundle whose constituent is missing `PrivateAssets="none"`
- **WHEN** `titi bundle lint` runs
- **THEN** exit code is 1 and the missing attribute is reported with the constituent package name

#### Scenario: Bundle independent versioning
- **GIVEN** a bundle with `versionStrategy: independent` in `bundles.yaml`
- **WHEN** constituent versions are re-evaluated during version planning
- **THEN** the bundle's version is bumped only if the bundle's externally visible dependency contract changes (for example, a constituent is added or removed, or the metapackage's recorded constituent version floor changes); internal-only bumps within constituents that do not change that contract do not cascade to the bundle. The bundle has its own changeset files to control explicit version bumps independent of constituent changes.
