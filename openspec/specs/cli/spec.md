# CLI

## Purpose

The CLI capability defines the command-line interface surface for titi, covering all Phase 1, Phase 2, and Phase 3 commands, their arguments, exit codes, and output behaviour.

## Requirements

### Requirement: titi open

The system SHALL implement `titi open <project>` which generates a transient .slnx solution file containing the target project and its swapped dependency closure, then optionally launches the configured IDE.

#### Scenario: Successful open
- **GIVEN** a valid project identifier and a warm or buildable graph
- **WHEN** `titi open Orion.Payments` is invoked
- **THEN** a transient .slnx is written to `.titi/solutions/`, refs are swapped, and (if autoOpen=true) the IDE is launched; exit code is 0

#### Scenario: Unknown project
- **WHEN** `titi open NonExistent.Project` is invoked
- **THEN** the command exits with code 1 and emits E001 (GRAPH_BUILD_FAILED)

### Requirement: titi affected

The system SHALL implement `titi affected` which computes the `AffectedSet` from current git changes relative to the configured base branch and prints affected project paths, one per line by default.

#### Scenario: Changes present
- **GIVEN** one or more modified source files belonging to tracked projects
- **WHEN** `titi affected` is run
- **THEN** each affected project path is printed to stdout and exit code is 0

#### Scenario: No changes
- **WHEN** git shows no changed files
- **THEN** no output is produced and exit code is 0

#### Scenario: JSON output
- **WHEN** `titi affected --output json` is invoked
- **THEN** a JSON object matching the `AffectedSet` schema is printed to stdout

### Requirement: titi clean

The system SHALL implement `titi clean` which removes all titi-generated artifacts under `.titi/`, including the graph cache and all transient solution files.

#### Scenario: Clean succeeds
- **GIVEN** `.titi/` contains a graph cache and solution files
- **WHEN** `titi clean` is invoked
- **THEN** `.titi/` directory is emptied (or removed) and exit code is 0

#### Scenario: Nothing to clean
- **WHEN** `.titi/` does not exist
- **THEN** the command exits with code 0 and reports nothing to clean

### Requirement: titi cache warm

The system SHALL implement `titi cache warm` which pre-builds and persists the full dependency graph to `.titi/graph.cache`, so subsequent commands can skip graph construction.

#### Scenario: Cache warmed
- **WHEN** `titi cache warm` is invoked on a valid monorepo
- **THEN** `.titi/graph.cache` is written and exit code is 0

#### Scenario: Cache warm with MSBuild unavailable
- **WHEN** `dotnet` is not on PATH
- **THEN** the command exits with code 1 and emits E007 (MSBUILD_NOT_FOUND)

### Requirement: titi build-manifest

The system SHALL implement `titi build-manifest` which generates a Traversal .proj XML file listing all projects in the affected change set with their `reason` and `tier`, suitable for `dotnet build` or `dotnet msbuild`. This command internally uses the same affected-set computation as `titi affected` but produces a build-ready Traversal project rather than a list of paths.

#### Scenario: Manifest generated
- **GIVEN** an affected set with directly affected and transitive projects
- **WHEN** `titi build-manifest` is run
- **THEN** a Traversal .proj is written containing `<ProjectReference>` items for each entry and exit code is 0

#### Scenario: Empty affected set
- **WHEN** no projects are affected
- **THEN** an empty Traversal .proj with no `<ProjectReference>` items is written

### Requirement: titi test-manifest

The system SHALL implement `titi test-manifest` which generates a Traversal .proj scoped to affected test projects, organised by tier.

#### Scenario: Test manifest by tier
- **GIVEN** affected unit and integration test projects
- **WHEN** `titi test-manifest` is run
- **THEN** a Traversal .proj is emitted containing all affected test projects, and exit code is 0

#### Scenario: Tier filter flag
- **WHEN** `titi test-manifest --tier unit` is invoked (valid values: `unit`, `package`, `integration`, `compatibility`)
- **THEN** only test projects matching the specified tier are included in the manifest

### Requirement: titi pkg

The system SHALL implement `titi pkg <add|remove|upgrade>` subcommands to manage `Directory.Packages.props`, adding, removing, or upgrading central package version entries.

#### Scenario: Add new package
- **WHEN** `titi pkg add Newtonsoft.Json 13.0.3` is invoked
- **THEN** a `<PackageVersion>` entry for `Newtonsoft.Json` with version `13.0.3` is added to `Directory.Packages.props`

#### Scenario: Upgrade existing package
- **WHEN** `titi pkg upgrade Newtonsoft.Json 13.0.4` is invoked
- **THEN** the existing `<PackageVersion>` entry is updated to `13.0.4`

#### Scenario: Remove package
- **WHEN** `titi pkg remove Newtonsoft.Json` is invoked
- **THEN** the `<PackageVersion>` entry is removed from `Directory.Packages.props`

#### Scenario: Package not found on remove
- **WHEN** `titi pkg remove UnknownPackage` is invoked
- **THEN** the command exits with code 1 and reports the package is not managed centrally

### Requirement: titi check

The system SHALL implement `titi check <package>` which checks whether the specified package's current local source version is compatible with all consuming projects in the monorepo.

#### Scenario: Compatible package
- **GIVEN** all consumers of `Orion.Core` are compatible with its current version
- **WHEN** `titi check Orion.Core` is run
- **THEN** exit code is 0 and a summary of compatible consumers is printed

#### Scenario: Incompatible consumer found
- **GIVEN** one consumer has a TFM incompatibility with the proposed version
- **WHEN** `titi check Orion.Core` is run
- **THEN** exit code is 1 and the incompatible consumers are listed with reasons

### Requirement: titi audit

The system SHALL implement `titi audit` which produces a transitive dependency audit report mapping each transitive package to its owning direct dependency and flagging vulnerabilities or version conflicts.

#### Scenario: Audit clean repo
- **GIVEN** no known vulnerabilities or version conflicts
- **WHEN** `titi audit` is run
- **THEN** exit code is 0 and the report states no issues found

#### Scenario: Conflict detected
- **GIVEN** two projects pull in conflicting transitive versions of the same package
- **WHEN** `titi audit` is run
- **THEN** exit code is 1 and the conflict is reported with owning projects and version ranges

### Requirement: titi version detect

The system SHALL implement `titi version detect [--dry-run] [--from <tag>] [--apply]` which runs the cascading bump algorithm over committed changesets, outputs a version plan showing each package's new version, and optionally writes the results to `version.json` files and `Directory.Packages.props`.

#### Scenario: Dry run outputs plan without writing
- **GIVEN** one or more changeset files are present in `.changesets/`
- **WHEN** `titi version detect --dry-run` is invoked
- **THEN** the computed version plan is printed to stdout and no files are modified; exit code is 0

#### Scenario: Apply writes version files
- **GIVEN** one or more changeset files are present in `.changesets/`
- **WHEN** `titi version detect --apply` is invoked
- **THEN** the version plan is applied by writing updated `version.json` files (via NBGV) for each affected package and updating `Directory.Packages.props` for CPM entries; exit code is 0

#### Scenario: From tag scopes detection
- **WHEN** `titi version detect --from v2.0.0` is invoked
- **THEN** only changesets merged after the `v2.0.0` tag are considered when computing the version plan

### Requirement: titi version validate

The system SHALL implement `titi version validate [--fix]` which verifies that AssemblyVersion follows the `{Major}.0.0.0` pattern, CPM is enabled, lock files are present, `RestoreLockedMode` is configured for CI, and `global.json` is present with an SDK version pin. With `--fix`, auto-correctable violations are applied in place.

#### Scenario: All checks pass
- **GIVEN** a correctly configured monorepo
- **WHEN** `titi version validate` is invoked
- **THEN** exit code is 0 and a summary confirms all checks passed

#### Scenario: Violations reported
- **GIVEN** one project has an incorrect AssemblyVersion pattern and `global.json` is absent
- **WHEN** `titi version validate` is invoked
- **THEN** exit code is 1 and each violation is listed with its file location and a remediation hint

#### Scenario: Fix applies safe corrections
- **WHEN** `titi version validate --fix` is invoked with auto-correctable violations present
- **THEN** safe fixes (e.g. correcting `AssemblyVersion` to `{Major}.0.0.0`) are written in place and non-auto-correctable violations are reported without modification

### Requirement: titi bundle create

The system SHALL implement `titi bundle create <name> --constituents LibA,LibB [--strategy independent|lockstep]` which scaffolds a metapackage `.csproj` referencing the specified constituent packages and registers the bundle in `bundles.yaml`.

#### Scenario: Bundle scaffolded
- **GIVEN** `titi bundle create Orion.Bundle --constituents Orion.Core,Orion.Data` is invoked
- **WHEN** the command completes
- **THEN** a metapackage `.csproj` for `Orion.Bundle` is created referencing `Orion.Core` and `Orion.Data` as constituents, an entry is written to `bundles.yaml`, and exit code is 0

#### Scenario: Independent strategy recorded
- **WHEN** `titi bundle create Orion.Bundle --constituents Orion.Core --strategy independent` is invoked
- **THEN** the `bundles.yaml` entry for `Orion.Bundle` records `versionStrategy: independent`

### Requirement: titi bundle check

The system SHALL implement `titi bundle check <name>` which reports any version drift between the bundle's declared version and the versions of its constituent packages.

#### Scenario: No drift
- **GIVEN** all constituents of a bundle are at their expected versions
- **WHEN** `titi bundle check Orion.Bundle` is invoked
- **THEN** exit code is 0 and a message confirms the bundle is in sync

#### Scenario: Drift detected
- **GIVEN** one constituent has been bumped independently of the bundle
- **WHEN** `titi bundle check Orion.Bundle` is invoked
- **THEN** exit code is 1 and the drifted constituent is listed with its current and expected versions

### Requirement: titi bundle update

The system SHALL implement `titi bundle update <name> [--dry-run]` which updates the bundle's version to reflect its current constituent versions, optionally previewing changes without writing them.

#### Scenario: Bundle version updated
- **GIVEN** a bundle with out-of-date version relative to its constituents
- **WHEN** `titi bundle update Orion.Bundle` is invoked
- **THEN** the bundle's version is updated in `bundles.yaml` and the metapackage `.csproj`, and exit code is 0

#### Scenario: Dry run previews without writing
- **WHEN** `titi bundle update Orion.Bundle --dry-run` is invoked
- **THEN** the proposed version update is printed and no files are modified; exit code is 0

### Requirement: titi bundle lint

The system SHALL implement `titi bundle lint [--all]` which scans bundle definitions for dual-reference anti-patterns (a consumer referencing both a bundle and one of its constituents directly) and stale bundles (constituents removed from the monorepo).

#### Scenario: Dual-reference anti-pattern detected
- **GIVEN** a consuming project references both `Orion.Bundle` and its constituent `Orion.Core` directly
- **WHEN** `titi bundle lint` is invoked
- **THEN** exit code is 1 and the dual-reference is reported with the consuming project name and the redundant constituent

#### Scenario: Stale bundle detected
- **GIVEN** a bundle references a constituent package that no longer exists in the monorepo
- **WHEN** `titi bundle lint --all` is invoked
- **THEN** the stale bundle entry is reported with the missing constituent name and exit code is 1

### Requirement: titi repl

The system SHALL implement `titi repl` which launches an interactive build REPL allowing the user to explore the dependency graph, run queries, and inspect node properties.

#### Scenario: REPL starts
- **WHEN** `titi repl` is invoked in a valid monorepo
- **THEN** an interactive prompt appears and exit code on quit is 0

#### Scenario: Graph query in REPL
- **GIVEN** the REPL is running
- **WHEN** the user enters a query for a project's dependents
- **THEN** the REPL prints the list of direct dependents for that project

### Requirement: Global CLI Flags

The system SHALL support the following global flags on the `titi` root command:
- `titi --help`: prints a summary of all available commands and exits with code 0
- `titi --version`: prints the current titi version string and exits with code 0
- Every subcommand SHALL support `--help`, printing that subcommand's usage and exiting with code 0

#### Scenario: Help flag exits cleanly
- **WHEN** `titi --help` is invoked
- **THEN** a command summary is printed to stdout and the process exits with code 0

#### Scenario: Version flag prints version string
- **WHEN** `titi --version` is invoked
- **THEN** the titi version string (e.g. `titi 1.2.3`) is printed to stdout and the process exits with code 0

#### Scenario: Subcommand help flag
- **WHEN** `titi open --help` is invoked
- **THEN** the usage description for `titi open` is printed to stdout and the process exits with code 0

### Requirement: Exit Codes

The system SHALL use exit code 0 for success, 1 for all command failures (including validation, graph, or build errors), and 2 for usage errors (invalid arguments or unknown subcommands).

#### Scenario: Successful command exit
- **WHEN** any titi command completes without errors
- **THEN** the process exits with code 0

#### Scenario: Command failure exit
- **WHEN** a titi command encounters a runtime error
- **THEN** the process exits with code 1

#### Scenario: Invalid argument exit
- **WHEN** an unrecognised flag is passed to any titi command
- **THEN** the process exits with code 2 and prints usage help to stderr
