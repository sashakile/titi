# Configuration

## Purpose

The configuration capability defines how titi reads, validates, and exposes its `titi.config.edn` file, including all sub-sections for cache, test tiers, IDE integration, and CI behaviour.

## Requirements

### Requirement CF-01: Config File Discovery

The system SHALL locate `titi.config.edn` by walking up from the current working directory to the git repository root, using the first file found.

#### Scenario: Config found at repo root
- **GIVEN** `titi.config.edn` exists at the git root
- **WHEN** any titi command is invoked from a subdirectory
- **THEN** the config is loaded from the repo root

#### Scenario: No config file found
- **WHEN** no `titi.config.edn` exists anywhere in the directory ancestry
- **THEN** the system applies built-in defaults (versionPolicy=SEMVER_COMPATIBLE, cache.enabled=true, cache.directory=".titi/") and emits a warn-level diagnostic suggesting the user run `titi init` to create an explicit config file

### Requirement CF-02: Core Configuration Fields

The system SHALL parse the `TitiConfig` root with required fields `prefix` (string, e.g. `"Orion."`), `sourceRoot` (path relative to repo root), and `versionPolicy` (STRICT | SEMVER_COMPATIBLE | FORCE, default: SEMVER_COMPATIBLE), and treat all other fields as optional with documented defaults. The config SHALL include a `schemaVersion` field (integer, default: 1). When the loaded `schemaVersion` is higher than the version supported by the running titi binary, the system SHALL emit E009 with a suggestion to upgrade titi. When the loaded `schemaVersion` is lower, the system SHALL apply forward-compatible defaults for any fields added in later schema versions.

#### Scenario: Valid minimal config
- **GIVEN** a config file with only `prefix`, `sourceRoot`, and `versionPolicy`
- **WHEN** the config is loaded
- **THEN** all three fields are populated and optional sections use defaults

#### Scenario: Missing required field
- **GIVEN** the config file omits `prefix`
- **WHEN** the config is loaded
- **THEN** the system emits E009 naming the missing field

### Requirement CF-03: Cache Configuration

The system SHALL read a `CacheConfig` sub-section specifying `enabled` (boolean, default: `true`), `directory` (default: `.titi/`), `maxAge` (duration string: integer followed by a unit suffix — `s` for seconds, `m` for minutes, `h` for hours, `d` for days; default: `"24h"`), and `globalTriggers` (list of file paths that force full graph invalidation, defaulting to `["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"]`).

> **Note:** `cache.directory` is the root artifact directory for ALL titi-generated files — including `graph.cache`, `solutions/`, `manifests/`, and `logs/` — not merely the location of the cache file itself.

#### Scenario: Cache disabled
- **GIVEN** `cache.enabled = false`
- **WHEN** any command requiring the graph runs
- **THEN** the graph is always rebuilt from scratch without reading or writing the cache file

#### Scenario: Custom global triggers
- **GIVEN** `cache.globalTriggers` includes `"global.json"`
- **WHEN** `global.json` is modified
- **THEN** the graph cache is fully invalidated on next use

### Requirement CF-04: Test Tier Configuration

The system SHALL read a `TestTierConfig` defining glob patterns for `unit`, `package`, `integration`, and `compatibility` test project tiers, plus a `defaultTier` for projects that match no pattern. Tier globs SHALL be evaluated in the order: `unit`, `package`, `integration`, `compatibility`; the first matching tier wins. The tiers are defined as:
- **unit**: isolated tests with no external dependencies
- **package**: tests for a library as consumers would use it (contract/package-level integration)
- **integration**: tests crossing service or domain boundaries
- **compatibility**: tests verifying a new version against existing consumers

#### Scenario: Project matches unit glob
- **GIVEN** `testTiers.unit = ["**/*.UnitTests.csproj"]`
- **WHEN** a project path matches that glob
- **THEN** the project is assigned to the `unit` tier in TieredTestSet

#### Scenario: Project matches no glob
- **GIVEN** a test project matching none of the configured globs
- **WHEN** the affected set is computed
- **THEN** the project is assigned to `defaultTier`

### Requirement CF-05: IDE Configuration

The system SHALL read an `IdeConfig` with `launchCommand` (executable path), `args` (argument template), and `autoOpen` (boolean) to control how `titi open` launches the IDE. The placeholder `{solution_path}` in `ide.args` is substituted with the absolute path of the generated `.slnx` file before the argument string is passed to the launch command.

#### Scenario: IDE auto-open enabled
- **GIVEN** `ide.autoOpen = true` and `ide.launchCommand = "rider"`
- **WHEN** `titi open <project>` completes solution generation
- **THEN** the system invokes `rider` with the generated .slnx path

#### Scenario: IDE auto-open disabled
- **GIVEN** `ide.autoOpen = false`
- **WHEN** `titi open <project>` runs
- **THEN** the .slnx is generated but no IDE process is spawned

#### Scenario: IDE launch command not found
- **GIVEN** `ide.autoOpen = true` and `ide.launchCommand = "rider"` but `rider` is not on PATH
- **WHEN** `titi open <project>` completes solution generation
- **THEN** the .slnx is generated, a warn-level diagnostic reports the launch failure, and the command exits with code 0

### Requirement CF-06: CI Configuration

The system SHALL read a `CiConfig` with `fullRegressionBranches` (list of branch name patterns), `maxParallelism` (integer), and `outputFormat` (`"json"` | `"text"` | `"github-actions"`). When a CLI `--output` flag is also provided, the CLI flag SHALL take precedence over `ci.outputFormat`; when both are absent, the default is `"text"` (see `diagnostics` spec, DX-04).

#### Scenario: Full regression on main
- **GIVEN** `ci.fullRegressionBranches = ["main", "release/*"]` and the current branch is `"main"`
- **WHEN** affected-set computation runs in CI
- **THEN** all projects are included regardless of changed files

#### Scenario: GitHub Actions output format
- **GIVEN** `ci.outputFormat = "github-actions"`
- **WHEN** a command produces output in CI
- **THEN** errors are formatted as `::error::` annotations

### Requirement CF-07: Config Validation

The system SHALL validate the loaded config on startup and surface all validation errors at once rather than failing on the first error found.

#### Scenario: Multiple validation errors reported together
- **GIVEN** a config with an invalid `versionPolicy` value and a non-existent `sourceRoot` path
- **WHEN** the config is loaded
- **THEN** both errors are reported in a single E009 diagnostic before the command aborts

### Requirement CF-08: External Prerequisites

The system SHALL document and validate the following minimum external tool versions at startup when the corresponding feature is used:
- **.NET SDK**: >= 8.0 (required for all MSBuild operations)
- **Nerdbank.GitVersioning (NBGV)**: >= 3.6 (required for `titi version detect/apply`)
- **Microsoft.DotNet.ApiCompat.Tool**: >= 8.0 (required for cascading bump API surface analysis)
- **git**: >= 2.25 (required for affected-set computation)

When a required tool is missing or below the minimum version, the system SHALL emit the appropriate error code (E007 for .NET SDK, E008 for git, E012 for ApiCompat) with a suggestion including the minimum required version.

#### Scenario: .NET SDK version too low
- **GIVEN** the installed .NET SDK is version 6.0
- **WHEN** any MSBuild-dependent command is invoked
- **THEN** E007 is emitted with a suggestion to upgrade to .NET SDK >= 8.0

#### Scenario: git version check
- **GIVEN** git is installed but at version 2.20
- **WHEN** `titi affected` is invoked
- **THEN** E008 is emitted with a suggestion to upgrade git to >= 2.25
