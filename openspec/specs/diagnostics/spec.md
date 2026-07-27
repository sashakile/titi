# Diagnostics

## Purpose

The diagnostics capability defines how titi surfaces errors, warnings, and structured events to users and downstream tooling, including the error code taxonomy, diagnostic event schema, and output formatting rules.

## Requirements

### Requirement DX-01: Structured Error Model

The system SHALL represent every user-facing error as a `TitiError` with a unique `ErrorCode`, a human-readable `message`, a `context` block (command, target, phase), and a `suggestions` list of actionable remediation steps. The `phase` field SHALL use a stable titi-defined identifier. Supported values include `command-parse`, `config-load`, `project-resolve`, `graph-build`, `cache-load`, `swap`, `solution-gen`, `manifest-gen`, `cleanup`, `package-manage`, `audit`, `bundle`, `repl`, `version-detect`, `version-validate`, `build`, and `test`; additional command-specific phases MAY be added in a backward-compatible manner when new capabilities are specified.

#### Scenario: Error includes suggestions
- **GIVEN** error E007 (MSBUILD_NOT_FOUND) is raised
- **WHEN** the error is displayed to the user
- **THEN** at least one suggestion (e.g. "Install the .NET SDK from https://dot.net") is shown alongside the error message

#### Scenario: Error includes context
- **GIVEN** E003 (VERSION_MISMATCH) is raised during `titi open`
- **WHEN** the error is displayed
- **THEN** the context block contains command="open", target=the affected package ID, and phase="swap"

### Requirement DX-02: Error Code Taxonomy

The system SHALL define and document the following error codes with their severity classification. **Fatal** errors abort the current command immediately; **aggregatable** errors are collected and reported together at the end of the current phase.

| Code | Name | Severity |
|------|------|----------|
| E001 | GRAPH_BUILD_FAILED | Fatal |
| E002 | CYCLE_DETECTED | Fatal |
| E003 | VERSION_MISMATCH | Aggregatable |
| E004 | TFM_INCOMPATIBLE | Aggregatable |
| E005 | NO_LOCAL_SOURCE | Aggregatable |
| E006 | CACHE_CORRUPT | Aggregatable |
| E007 | MSBUILD_NOT_FOUND | Fatal |
| E008 | GIT_ENVIRONMENT_ERROR | Fatal |
| E009 | CONFIG_INVALID | Aggregatable |
| E010 | BUILD_FAILED | Fatal |
| E011 | TEST_FAILED | Aggregatable |
| E012 | APICOMPAT_NOT_AVAILABLE | Aggregatable |
| E013 | NBGV_NOT_FOUND | Fatal |
| E014 | VERSION_PARSE_INVALID | Aggregatable |
| E999 | INTERNAL_ERROR | Fatal |

> **Relationship to `RetainedReason`:** The swap engine (see `reference-swap` spec, RS-01) uses a separate `RetainedReason` enumeration with overlapping names (`VERSION_MISMATCH`, `TFM_INCOMPATIBLE`, `NO_LOCAL_SOURCE`, `CYCLE_PREVENTION`, `TRANSITIVE_FLOOR_UNSATISFIED`). A `RetainedReason` is a planning-time classification describing why a swap was not performed; it is NOT a `TitiError` and does NOT by itself produce an error code. A `TitiError` is raised only when a bypassable safety check fails AND that check is NOT in the `overrides` set. `RetainedReason.NO_LOCAL_SOURCE` is purely informational (no local source candidate exists), is never bypassable, and never produces E005.
>
> | RetainedReason | In `overrides`? | Emitted `TitiError` code |
> |---|---|---|
> | `VERSION_MISMATCH` | no | E003 |
> | `VERSION_MISMATCH` | yes | none (warn diagnostic) |
> | `TFM_INCOMPATIBLE` | no | E004 |
> | `TFM_INCOMPATIBLE` | yes | none (warn diagnostic) |
> | `CYCLE_PREVENTION` | no | E002 (via `CycleReport`) |
> | `CYCLE_PREVENTION` | yes | none (warn diagnostic) |
> | `TRANSITIVE_FLOOR_UNSATISFIED` | no | none (diagnostic; no dedicated code) |
> | `TRANSITIVE_FLOOR_UNSATISFIED` | yes | none (warn diagnostic) |
> | `NO_LOCAL_SOURCE` | n/a (never bypassable) | none (informational; never E005) |
>
> E005 (`NO_LOCAL_SOURCE`) is reserved in the taxonomy for symmetry but is not raised by the swap engine; `titi check` (CLI-08) raises E003/E004 when a project is reported as incompatible.

> **Reserved codes:** E010 (BUILD_FAILED) and E011 (TEST_FAILED) are reserved for future build/test capabilities and SHALL NOT be raised by the current CLI surface.

Additional error codes SHALL NOT be added without a corresponding spec update to this taxonomy.

> **E009 vs E014:** E009 (`CONFIG_INVALID`) is reserved for errors in `titi.config.edn` (missing required fields, invalid values, unsupported `schemaVersion`). E014 (`VERSION_PARSE_INVALID`) is raised when a version string fails to parse into a `SemanticVersion` or `NuGetVersionRange` (see `domain-model` spec, DM-02 and DM-04). The two codes are distinct: a config error is an authoring problem in `titi.config.edn`; a version-parse error is an authoring problem in a `.csproj`/`version.json`/`Directory.Packages.props` value.

#### Scenario: Known error code emitted
- **WHEN** the graph build fails due to a malformed .csproj
- **THEN** the error carries code E001

#### Scenario: Unknown error not silenced
- **WHEN** an unexpected internal exception occurs that does not map to any specific error code (E001–E014)
- **THEN** the exception is wrapped in a TitiError with code E999 (INTERNAL_ERROR), the exception message as `message`, and the current `phase` in the context block, rather than producing an unformatted stack trace in production output

#### Scenario: ApiCompat not available
- **WHEN** `titi version detect` requires API compatibility analysis but `Microsoft.DotNet.ApiCompat.Tool` is not installed or the baseline assembly cannot be obtained
- **THEN** the error carries code E012 with a suggestion to install the ApiCompat tool or ensure the baseline package version is available in the configured NuGet feed

### Requirement DX-03: Diagnostic Event Stream

The system SHALL emit `DiagnosticEvent` records during command execution, each containing `timestamp`, `level` (debug/info/warn/error), `source`, `message`, optional `data` map, and optional `durationMs`.

#### Scenario: Debug events suppressed by default
- **WHEN** a command runs without a verbose flag
- **THEN** events with level=debug are not written to stdout or stderr

#### Scenario: Verbose mode shows debug events
- **WHEN** `--verbose` is passed (see `cli` spec, CLI-17)
- **THEN** all diagnostic events including level=debug are written to stderr

#### Scenario: Duration captured for slow operations
- **GIVEN** a graph build taking more than 100 ms
- **WHEN** the build completes
- **THEN** the diagnostic event for the build step includes a non-zero `durationMs`

### Requirement DX-04: Output Format Selection

The system SHALL support three output formats for diagnostic and command output: `"text"` (human-readable, default), `"json"` (machine-readable structured output), and `"github-actions"` (GitHub Actions workflow command annotations). The CLI `--output` flag takes precedence over `ci.outputFormat` in config (see `configuration` spec, CF-06); when neither is specified, the default is `"text"`.

#### Scenario: Text format default
- **WHEN** a command runs without an explicit format flag
- **THEN** output is plain human-readable text

#### Scenario: JSON format
- **WHEN** `--output json` is passed
- **THEN** the primary command result and any errors are emitted as a JSON object conforming to the relevant schema type

#### Scenario: GitHub Actions format
- **WHEN** `--output github-actions` is passed (or `ci.outputFormat = "github-actions"` in config)
- **THEN** errors are emitted as `::error file=<path>::<message>` and warnings as `::warning file=<path>::<message>` annotations

### Requirement DX-05: Error Aggregation

The system SHALL collect and report all errors encountered during a command rather than aborting on the first error, wherever safe to do so. Specifically: errors with **aggregatable** severity (see DX-02) are collected during a phase and reported together before the command exits. Errors with **fatal** severity abort the current command immediately after emission, as continued execution would operate on invalid state.

#### Scenario: Multiple validation errors shown together
- **GIVEN** three projects have version validation issues
- **WHEN** `titi version validate` runs
- **THEN** all three issues are reported before the command exits with code 1

#### Scenario: Fatal errors abort immediately
- **GIVEN** `dotnet` is not available on PATH and a command requires MSBuild evaluation (E007, severity: fatal)
- **WHEN** the command is invoked
- **THEN** the command aborts immediately after emitting E007, rather than attempting to continue with invalid prerequisites

### Requirement DX-06: Actionable Suggestion Quality

The system SHALL ensure every `TitiError.suggestions` entry is a concrete, executable action (e.g. a specific CLI command to run or a file to edit), not a vague description.

#### Scenario: Suggestion is a runnable command
- **GIVEN** E009 (CONFIG_INVALID) is raised because `titi.config.edn` is missing
- **WHEN** the error is displayed
- **THEN** suggestions include the exact command `titi init` to create the config file
