# Diagnostics

## Purpose

The diagnostics capability defines how titi surfaces errors, warnings, and structured events to users and downstream tooling, including the error code taxonomy, diagnostic event schema, and output formatting rules.

## Requirements

### Requirement DX-01: Structured Error Model

The system SHALL represent every user-facing error as a `TitiError` with a unique `ErrorCode`, a human-readable `message`, a `context` block (command, target, phase), and a `suggestions` list of actionable remediation steps. Valid values for the `phase` field are: `config-load`, `graph-build`, `cache-load`, `swap`, `solution-gen`, `manifest-gen`, `version-detect`, `build`, `test`.

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
| E006 | CACHE_CORRUPT | Fatal |
| E007 | MSBUILD_NOT_FOUND | Fatal |
| E008 | GIT_NOT_AVAILABLE | Fatal |
| E009 | CONFIG_INVALID | Aggregatable |
| E010 | BUILD_FAILED | Fatal |
| E011 | TEST_FAILED | Aggregatable |
| E012 | APICOMPAT_NOT_AVAILABLE | Aggregatable |
| E999 | INTERNAL_ERROR | Fatal |

Additional error codes SHALL NOT be added without a corresponding spec update to this taxonomy.

#### Scenario: Known error code emitted
- **WHEN** the graph build fails due to a malformed .csproj
- **THEN** the error carries code E001

#### Scenario: Unknown error not silenced
- **WHEN** an unexpected internal exception occurs that does not map to any specific error code (E001–E012)
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
- **GIVEN** the graph cache is corrupt (E006, severity: fatal)
- **WHEN** the graph is loaded
- **THEN** the command aborts immediately after emitting E006, rather than attempting to continue with a partial graph

### Requirement DX-06: Actionable Suggestion Quality

The system SHALL ensure every `TitiError.suggestions` entry is a concrete, executable action (e.g. a specific CLI command to run or a file to edit), not a vague description.

#### Scenario: Suggestion is a runnable command
- **GIVEN** E009 (CONFIG_INVALID) is raised because `titi.config.edn` is missing
- **WHEN** the error is displayed
- **THEN** suggestions include the exact command `titi init` to create the config file
