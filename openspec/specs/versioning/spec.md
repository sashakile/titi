# Versioning

## Purpose

The versioning capability covers NuGet version resolution semantics, Central Package Management (CPM) conventions, cascading version bump propagation, changeset-based workflows, AssemblyVersion strategy, and bundle/metapackage management.

## Requirements

### Requirement: NBGV Integration

The system SHALL integrate with Nerdbank.GitVersioning (NBGV). Version detection SHALL read the current version from `version.json` files via NBGV. Version apply SHALL write updated `version.json` files. Projects without a `version.json` SHALL NOT be managed by titi version commands.

#### Scenario: Version read from version.json
- **GIVEN** a project with a `version.json` file managed by NBGV
- **WHEN** `titi version detect` runs
- **THEN** the current version is read from that project's `version.json` and used as the base for bump calculations

#### Scenario: Project without version.json skipped
- **GIVEN** a project with no `version.json` file
- **WHEN** `titi version detect --apply` runs
- **THEN** the project is skipped and a diagnostic note indicates it is not managed by titi version commands

### Requirement: NuGet Lowest-Applicable-Version Resolution

The system SHALL document and enforce that NuGet uses lowest-applicable-version resolution: a `PackageReference Version="1.0.0"` means `>= 1.0.0`, not "exactly 1.0.0", and an exact pin requires the interval syntax `[1.0.0]`.

#### Scenario: Floating minimum resolved to lowest
- **GIVEN** `PackageReference Version="2.0.0"` and available versions 2.0.0, 2.1.0, 3.0.0
- **WHEN** restore runs
- **THEN** version 2.0.0 is selected (lowest applicable)

#### Scenario: Exact pin respected
- **GIVEN** `PackageReference Version="[2.0.0]"`
- **WHEN** restore runs
- **THEN** only version 2.0.0 is accepted; restore fails if 2.0.0 is unavailable

### Requirement: Central Package Management

The system SHALL require `ManagePackageVersionsCentrally=true` in `Directory.Packages.props` and MUST enable `CentralPackageTransitivePinningEnabled=true` to establish a transitive version floor across the monorepo.

#### Scenario: CPM enabled
- **GIVEN** `Directory.Packages.props` has `ManagePackageVersionsCentrally=true`
- **WHEN** any project in the repo is built
- **THEN** `<PackageReference>` items in .csproj files MUST NOT specify a `Version` attribute

#### Scenario: Transitive pinning enforced
- **GIVEN** `CentralPackageTransitivePinningEnabled=true`
- **WHEN** a transitive dependency would otherwise resolve to a version below the floor set in `Directory.Packages.props`
- **THEN** the floor version is used instead

### Requirement: Lock File Management

The system SHALL enforce `RestorePackagesWithLockFile=true` in `Directory.Packages.props` and require `RestoreLockedMode=true` in CI environments only, ensuring reproducible restores in CI while allowing lock file updates locally.

#### Scenario: Locked restore in CI
- **GIVEN** `RestoreLockedMode=true` in CI and a lock file present
- **WHEN** `dotnet restore` runs
- **THEN** restore succeeds only if the lock file matches the current dependency graph

#### Scenario: Lock file regeneration after swap
- **WHEN** `titi pkg upgrade` modifies `Directory.Packages.props`
- **THEN** the system runs `dotnet restore --force-evaluate` to regenerate the lock file

### Requirement: NuGet 6.12 CPM Regression Workaround

The system SHALL apply `RestoreUseLegacyDependencyResolver=true` in `Directory.Packages.props` when CPM transitive pinning is enabled, to avoid false NU1605 warnings introduced by the NuGet 6.12 regression.

#### Scenario: Workaround applied
- **GIVEN** `CentralPackageTransitivePinningEnabled=true`
- **WHEN** `Directory.Packages.props` is validated
- **THEN** `RestoreUseLegacyDependencyResolver=true` is present

#### Scenario: titi version validate reports missing workaround
- **GIVEN** `CentralPackageTransitivePinningEnabled=true` but `RestoreUseLegacyDependencyResolver` is absent
- **WHEN** `titi version validate` runs
- **THEN** a warning is emitted recommending the workaround

### Requirement: AssemblyVersion Major-Only Strategy

The system SHALL require that all projects in the monorepo set `AssemblyVersion` to `{Major}.0.0.0`, retaining only the major component to maximise binary compatibility across minor and patch releases.

#### Scenario: Correct AssemblyVersion pattern
- **GIVEN** a project with `<Version>3.7.2</Version>`
- **WHEN** `titi version validate` runs
- **THEN** no error is reported if `AssemblyVersion` is `3.0.0.0`

#### Scenario: Incorrect AssemblyVersion pattern
- **GIVEN** a project with `<AssemblyVersion>3.7.2.0</AssemblyVersion>`
- **WHEN** `titi version validate` runs
- **THEN** a validation error is emitted specifying the expected `{Major}.0.0.0` pattern

### Requirement: Cascading Bump Algorithm

The system SHALL implement a cascading version bump algorithm that: builds the dependency graph, identifies changed packages, determines the bump type (patch/minor/major) per package using `Microsoft.DotNet.ApiCompat`, topologically propagates the bump only where the API surface has changed, and applies the highest bump type when multiple propagation paths converge.

The algorithm proceeds as follows:
1. For each changed package, run `Microsoft.DotNet.ApiCompat.Tool` comparing the baseline (last published) assembly against the locally built assembly to determine whether the public API surface has changed.
2. If ApiCompat reports no public API differences, the change is internal-only — the package receives the bump from its changeset but does **not** propagate to dependents.
3. If ApiCompat reports additive-only changes (new public types/members), propagate a **minor** bump to direct dependents.
4. If ApiCompat reports breaking changes (removed/changed public types/members), propagate a **major** bump to direct dependents.
5. Propagation continues topologically: each dependent is re-evaluated via ApiCompat only if it received a propagated bump. If the dependent's own public API is unchanged by the new version of its dependency, propagation stops at that node.
6. When multiple propagation paths converge on a single package, the highest bump type wins.

#### Scenario: Internal-only patch does not propagate
- **GIVEN** package A has a patch changeset and ApiCompat confirms no public API surface change between the baseline and current build
- **WHEN** the cascading bump runs
- **THEN** A receives a patch bump; downstream packages of A are **not** bumped because A's public API is unchanged

#### Scenario: Minor API addition propagates through chain
- **GIVEN** package A has a minor changeset, ApiCompat reports additive-only public API changes, and B depends on A
- **WHEN** the cascading bump runs
- **THEN** A receives a minor bump and B receives a minor bump (because A's public API changed)

#### Scenario: Propagation stops when dependent API is unchanged
- **GIVEN** package A has a minor bump that propagates to B, but B's own public API surface is unchanged (ApiCompat reports no differences for B)
- **WHEN** the cascading bump runs
- **THEN** A receives a minor bump, B receives a minor bump, but C (which depends on B) is **not** bumped

#### Scenario: Highest bump wins at convergence
- **GIVEN** package C is reached via two paths: one requiring patch and one requiring minor
- **WHEN** bumps are applied
- **THEN** C receives a minor bump

### Requirement: Changeset File Format

Changeset files SHALL live in the `.changesets/` directory at the repository root. Each file must be a `.yaml` file (filename convention: `<timestamp>-<package-id>.yaml`, though any `.yaml` file in the directory is accepted). Required fields are:
- `package`: the package ID (e.g. `Orion.Core.Data`)
- `bump`: one of `patch`, `minor`, or `major`
- `description`: a human-readable summary of the change

Changeset files are created manually by the developer per PR. Example:

```yaml
package: Orion.Core.Data
bump: minor
description: Add async overloads to IDataService
```

#### Scenario: titi version detect reads changeset files
- **GIVEN** two changeset files exist in `.changesets/`: one specifying `Orion.Core.Data` with `bump: minor` and one specifying `Orion.Core.Data` with `bump: patch`
- **WHEN** `titi version detect` runs
- **THEN** `Orion.Core.Data` receives a minor bump (highest wins) and the version plan reflects this

### Requirement: Changeset-Based Workflow

The system SHALL support a changeset-based versioning workflow where each PR includes a changeset file specifying the affected packages and their bump types, and `titi version detect` aggregates changesets to compute final version increments.

#### Scenario: Changeset aggregated
- **GIVEN** two changesets in the current PR: one specifying `Orion.Core` minor and one specifying `Orion.Core` patch
- **WHEN** `titi version detect` runs
- **THEN** `Orion.Core` receives a minor bump (highest wins)

#### Scenario: Dry run
- **WHEN** `titi version detect --dry-run` is invoked
- **THEN** the computed version increments are printed but no files are modified

#### Scenario: Apply flag writes versions
- **WHEN** `titi version detect --apply` is invoked
- **THEN** the computed version increments are written to the `version.json` files managed by Nerdbank.GitVersioning (NBGV) for each affected package, and `Directory.Packages.props` is updated for any CPM-pinned entries

### Requirement: titi version validate

The system SHALL implement `titi version validate` which checks: AssemblyVersion pattern, CPM enabled, lock files present, `RestoreLockedMode` present in CI config, `global.json` SDK version pinned, and no suppressed NU1605 warnings.

#### Scenario: All checks pass
- **GIVEN** a correctly configured monorepo
- **WHEN** `titi version validate` runs
- **THEN** exit code is 0 and "All version checks passed" is reported

#### Scenario: Violations found
- **GIVEN** one project has an incorrect AssemblyVersion and another suppresses NU1605
- **WHEN** `titi version validate` runs
- **THEN** exit code is 1 and each violation is listed with its location and remediation hint

#### Scenario: Fix flag applied
- **WHEN** `titi version validate --fix` is invoked
- **THEN** auto-correctable violations (e.g. incorrect AssemblyVersion pattern) are fixed in-place and non-auto-correctable violations are reported

### Requirement: Bundle and Metapackage Management

The system SHALL support bundle (metapackage) definitions via `bundles.yaml` at the repo root, with commands `titi bundle create|check|update|lint` managing bundle composition, constituent visibility, and independent versioning.

#### Scenario: Bundle creation
- **WHEN** `titi bundle create <name>` is invoked with a list of constituent packages
- **THEN** an entry is added to `bundles.yaml` with `PrivateAssets="none"` for each constituent and `IncludeBuildOutput=false` on the bundle project

#### Scenario: Bundle lint detects misconfiguration
- **GIVEN** a bundle whose constituent is missing `PrivateAssets="none"`
- **WHEN** `titi bundle lint` runs
- **THEN** exit code is 1 and the missing attribute is reported with the constituent package name

#### Scenario: Bundle independent versioning
- **GIVEN** a bundle with `versionStrategy: independent` in `bundles.yaml`
- **WHEN** the cascading bump runs
- **THEN** the bundle's version is bumped only if a constituent's public API surface changed (as detected by ApiCompat on the bundle's own public API); internal-only bumps within constituents do not cascade to the bundle. The bundle has its own changeset files to control explicit version bumps independent of constituent changes.
