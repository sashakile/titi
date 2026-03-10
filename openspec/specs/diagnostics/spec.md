# Diagnostics

## Purpose

The diagnostics capability defines how titi surfaces errors, warnings, and structured events to users and downstream tooling, including the error code taxonomy, diagnostic event schema, and output formatting rules.

## Requirements

### Requirement: Structured Error Model

The system SHALL represent every user-facing error as a `TitiError` with a unique `ErrorCode`, a human-readable `message`, a `context` block (command, target, phase), and a `suggestions` list of actionable remediation steps.

#### Scenario: Error includes suggestions
- **GIVEN** error E007 (MSBUILD_NOT_FOUND) is raised
- **WHEN** the error is displayed to the user
- **THEN** at least one suggestion (e.g. "Install the .NET SDK from https://dot.net") is shown alongside the error message

#### Scenario: Error includes context
- **GIVEN** E003 (VERSION_MISMATCH) is raised during `titi open`
- **WHEN** the error is displayed
- **THEN** the context block contains command="open", target=the affected package ID, and phase="swap"

### Requirement: Error Code Taxonomy

The system SHALL define and document the following error codes: E001 GRAPH_BUILD_FAILED, E002 CYCLE_DETECTED, E003 VERSION_MISMATCH, E004 TFM_INCOMPATIBLE, E005 NO_LOCAL_SOURCE, E006 CACHE_CORRUPT, E007 MSBUILD_NOT_FOUND, E008 GIT_NOT_AVAILABLE, E009 CONFIG_INVALID, E010 BUILD_FAILED, E011 TEST_FAILED. No other error codes SHALL be used for structured errors.

#### Scenario: Known error code emitted
- **WHEN** the graph build fails due to a malformed .csproj
- **THEN** the error carries code E001

#### Scenario: Unknown error not silenced
- **WHEN** an unexpected internal exception occurs
- **THEN** the error is wrapped with a structured TitiError (using the most applicable code) rather than producing an unformatted stack trace in production output

### Requirement: Diagnostic Event Stream

The system SHALL emit `DiagnosticEvent` records during command execution, each containing `timestamp`, `level` (debug/info/warn/error), `source`, `message`, optional `data` map, and optional `durationMs`.

#### Scenario: Debug events suppressed by default
- **WHEN** a command runs without a verbose flag
- **THEN** events with level=debug are not written to stdout or stderr

#### Scenario: Verbose mode shows debug events
- **WHEN** `--verbose` (or equivalent) is passed
- **THEN** all diagnostic events including level=debug are written to stderr

#### Scenario: Duration captured for slow operations
- **GIVEN** a graph build taking more than 100 ms
- **WHEN** the build completes
- **THEN** the diagnostic event for the build step includes a non-zero `durationMs`

### Requirement: Output Format Selection

The system SHALL support three output formats for diagnostic and command output: `"text"` (human-readable, default), `"json"` (machine-readable structured output), and `"github-actions"` (GitHub Actions workflow command annotations).

#### Scenario: Text format default
- **WHEN** a command runs without an explicit format flag
- **THEN** output is plain human-readable text

#### Scenario: JSON format
- **WHEN** `--output json` is passed
- **THEN** the primary command result and any errors are emitted as a JSON object conforming to the relevant schema type

#### Scenario: GitHub Actions format
- **WHEN** `--output github-actions` is passed (or `ci.outputFormat = "github-actions"` in config)
- **THEN** errors are emitted as `::error file=<path>::<message>` and warnings as `::warning file=<path>::<message>` annotations

### Requirement: Error Aggregation

The system SHALL collect and report all errors encountered during a command rather than aborting on the first error, wherever safe to do so (e.g., validation passes, affected-set computation).

#### Scenario: Multiple validation errors shown together
- **GIVEN** three projects have version validation issues
- **WHEN** `titi version validate` runs
- **THEN** all three issues are reported before the command exits with code 1

#### Scenario: Fatal errors abort immediately
- **GIVEN** the graph cache is corrupt (E006)
- **WHEN** the graph is loaded
- **THEN** the command aborts immediately after emitting E006, rather than attempting to continue with a partial graph

### Requirement: Actionable Suggestion Quality

The system SHALL ensure every `TitiError.suggestions` entry is a concrete, executable action (e.g. a specific CLI command to run or a file to edit), not a vague description.

#### Scenario: Suggestion is a runnable command
- **GIVEN** E009 (CONFIG_INVALID) is raised because `titi.config.edn` is missing
- **WHEN** the error is displayed
- **THEN** suggestions include the exact command `titi init` to create the config file
