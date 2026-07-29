# CLI

## Purpose

The CLI capability defines the command-line interface surface for titi, covering all Phase 1, Phase 2, and Phase 3 commands, their arguments, exit codes, and output behaviour.

> **Path convention:** Paths follow `.titi/` as the artifact directory. The `cache.directory` config field is aspirational (future release); currently `.titi/` is hard-coded.

> **Structural note — capability scope:** This spec owns the *CLI surface* (argument parsing, exit codes, output formatting) for every titi command. Only CLI-wide concerns (global flags CLI-17, exit codes CLI-18, output format) are owned outright here.

## Requirements

### Requirement CLI-01: titi open

The system SHALL implement `titi open <package-id>` which generates a transient .slnx solution file containing the target project and its swapped dependency closure, then optionally launches the configured IDE.

#### Scenario: Open by package id
- **GIVEN** a monorepo with package `Orion.Core.Data`
- **WHEN** `titi open Orion.Core.Data` runs
- **THEN** a .slnx file is generated at an output path printed to stdout as JSON (`{"solutionPath": "...", "projectCount": N}`)

#### Scenario: IDE auto-open disabled
- **GIVEN** `ide.autoOpen = false` in config
- **WHEN** `titi open` completes
- **THEN** the .slnx is generated but no IDE process is spawned

### Requirement CLI-02: titi affected

The system SHALL implement `titi affected [--base <ref>]` to compute the set of projects affected by changes since a base git reference. Output is JSON by default.

#### Scenario: Affected with default base
- **GIVEN** a repository with local changes
- **WHEN** `titi affected` is invoked
- **THEN** the affected set is printed as a JSON object to stdout

#### Scenario: Affected with explicit base
- **GIVEN** a repository with changes since `HEAD~3`
- **WHEN** `titi affected --base HEAD~3` is invoked
- **THEN** changes relative to `HEAD~3` are used

### Requirement CLI-03: titi clean

The system SHALL implement `titi clean` which removes `.titi/` and all generated artifacts.

#### Scenario: Clean removes artifacts
- **GIVEN** a `.titi/` directory exists
- **WHEN** `titi clean` runs
- **THEN** the `.titi/` directory and all its contents are removed

### Requirement CLI-04: titi tests list

The system SHALL implement `titi tests list <csproj>` which discovers test items in the specified test project.

#### Scenario: List tests in project
- **GIVEN** a test project path
- **WHEN** `titi tests list tests/Orion.UnitTests/Orion.UnitTests.csproj` runs
- **THEN** discovered test items are listed to stdout

### Requirement CLI-05: titi tests ingest

The system SHALL implement `titi tests ingest <trx-path> [--coverage <cobertura-path>]` which ingests a TRX test results file and optionally a Cobertura coverage report.

#### Scenario: Ingest with TRX only
- **GIVEN** a TRX file from a test run
- **WHEN** `titi tests ingest results.trx` runs
- **THEN** test results are ingested and edges are built

### Requirement CLI-06: titi tests record

The system SHALL implement `titi tests record` which runs all test projects with coverage, ingests results, and builds the edge index.

#### Scenario: Record runs and builds edges
- **GIVEN** a monorepo with test projects
- **WHEN** `titi tests record` runs
- **THEN** all tests execute, results are ingested, and the edge index is built

### Requirement CLI-07: titi test-manifest

The system SHALL implement `titi test-manifest [--tier <tier>] [--select] [--list]` which generates a Traversal .proj file for affected test projects.

#### Scenario: Generate test manifest
- **GIVEN** a monorepo with affected test projects
- **WHEN** `titi test-manifest --tier unit` runs
- **THEN** a .proj file is generated in `.titi/test-manifests/`

#### Scenario: Generate per-test filtered manifest
- **GIVEN** an existing test-edge cache
- **WHEN** `titi test-manifest --select --list` runs
- **THEN** a .proj file with per-test filters is generated

### Requirement CLI-08: titi testaruda-adapter

The system SHALL implement `titi testaruda-adapter` which starts the testaruda adapter subprocess. Communication is JSON-over-stdio: one JSON request per line on stdin, one JSON response per line on stdout. The adapter handles the following commands: handshake, discover, static-deps, fingerprint, run-args, ingest, shutdown.

#### Scenario: Adapter handshake
- **GIVEN** the adapter process is started
- **WHEN** `{"command":"handshake","params":{}}` is sent to stdin
- **THEN** a JSON response with adapter metadata (name, version, protocol, languages, granularity, capabilities) is returned on stdout

#### Scenario: Adapter shutdown
- **GIVEN** the adapter process is running
- **WHEN** `{"command":"shutdown"}` is sent to stdin
- **THEN** the adapter responds and exits cleanly

### Requirement CLI-09: titi repl

The system SHALL implement `titi repl` which starts an interactive REPL for graph queries and exploration. The REPL accepts commands via stdin and prints results to stdout.

#### Scenario: REPL starts
- **WHEN** `titi repl` is invoked
- **THEN** the REPL prompt is shown and commands can be entered

### Requirement CLI-10: titi --help

The system SHALL implement `titi --help` (or `-h`, or no arguments) which prints a summary of all available commands and their usage.

#### Scenario: Help displays all commands
- **WHEN** `titi --help` is invoked
- **THEN** a command summary is printed to stdout and the process exits with code 0

### Requirement CLI-11: Unknown and invalid commands

The system SHALL print an error to stderr and exit with code 2 when an unknown command, unknown flag, or missing required argument is encountered.

#### Scenario: Unknown command
- **WHEN** `titi nonexistent-command` is invoked
- **THEN** "Unknown command: nonexistent-command" is printed to stderr and exit code is 2

### Requirement CLI-13: Exit Codes

The system SHALL use exit code 0 for success, 1 for all command failures (including validation, graph, or build errors), and 2 for usage errors (invalid arguments or unknown subcommands).

#### Scenario: Successful command exit
- **WHEN** any titi command completes without errors
- **THEN** the process exits with code 0

#### Scenario: Command failure exit
- **WHEN** a titi command encounters a runtime error
- **THEN** the process exits with code 1

#### Scenario: Invalid argument exit
- **WHEN** an unrecognised flag or unknown command is used
- **THEN** the process exits with code 2 and prints usage help to stderr
