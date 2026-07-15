## ADDED Requirements

### Requirement: titi testaruda-adapter (CLI-19)

The system SHALL implement `titi testaruda-adapter` which starts a long-lived adapter process that speaks the testaruda adapter protocol (JSON request/response over stdin/stdout) and serves as testaruda's C#/.NET static-dependency source. The adapter SHALL run as a single long-lived process for the duration of one testaruda invocation (builds or loads the `MonorepoGraph` once, answers all protocol commands from memory, never re-acquires the writer lock on `.titi/graph.cache` at query time), contingent on confirmation from testaruda's `AdapterIO` implementation that this model is supported (see design.md Decision 2). If `.titi/graph.cache` is corrupt or from an incompatible version, the adapter SHALL rebuild the graph from scratch. The adapter SHALL clean up resources (close open file handles, release any locks) and exit with code 0 when stdin is closed or a `shutdown` command is received. The adapter SHALL only activate when explicitly invoked; it SHALL NOT affect any existing titi command when not invoked. Phase 1 SHALL use project-level granularity (`symbol_model_complete: false`, each test item is a whole test assembly).

#### Scenario: Adapter starts and handshake succeeds
- **WHEN** testaruda spawns `titi testaruda-adapter` as a subprocess
- **THEN** the adapter prints a JSON handshake response to stdout with name=`titi`, languages=`["csharp"]`, granularity=`project`, and `{symbol_model_complete: false}`, and waits for further commands on stdin

#### Scenario: Discover emits test projects
- **GIVEN** a monorepo with 3 test projects and 5 library projects
- **WHEN** testaruda sends a `discover` command
- **THEN** the adapter emits 3 test items, one per test project, each with project path and tier metadata from `TestTierConfig`

#### Scenario: Discover returns empty list for zero test projects
- **GIVEN** a monorepo with no test projects
- **WHEN** testaruda sends a `discover` command
- **THEN** the adapter returns an empty test item list

#### Scenario: Static-deps uses titi's AffectedSet
- **GIVEN** a changed-file set that affects 1 test project via transitive dependency
- **WHEN** testaruda sends a `static-deps` command with the changed-file set
- **THEN** the adapter returns that 1 test project as affected, with K-value = multiplicative identity, reusing titi's `AffectedSet` computation (DG-04)

> **Note:** This scenario assumes the whole-graph adapter model (design.md Decision 4, option b). If the per-component model (option a) is adopted, the adapter's `static-deps` response would be scoped per-component and the response shape would differ.

#### Scenario: Fingerprint reuses FileFingerprint
- **GIVEN** a warm graph with fingerprint data
- **WHEN** testaruda sends a `fingerprint` command
- **THEN** the adapter returns the fingerprint data from `MonorepoGraph.fingerprints` (DG-07) without modification

#### Scenario: Run-args generates traversal project
- **GIVEN** a set of affected test projects
- **WHEN** testaruda sends a `run-args` command
- **THEN** the adapter returns `["dotnet", "test", "<generated-traversal.proj>"]` using the same Traversal-.proj logic as `titi test-manifest` (CLI-06), without executing the tests

#### Scenario: Ingest parses TRX output
- **GIVEN** a TRX file produced by `dotnet test --logger trx`
- **WHEN** testaruda sends an `ingest` command with the TRX file path
- **THEN** the adapter returns per-test PASS/FAIL/duration results parsed from the TRX

#### Scenario: Malformed ingest input falls back safely
- **WHEN** testaruda sends an `ingest` command with a malformed or unparseable TRX file
- **THEN** the adapter returns an error response, and testaruda's core falls back to all-tests selection per TIA-ADAPT-012

#### Scenario: Unexpected command order returns error
- **GIVEN** the adapter is past handshake but the graph has not been fully initialized (e.g., no `discover` call received yet)
- **WHEN** testaruda sends a `static-deps` or `fingerprint` command
- **THEN** the adapter returns an error response and continues waiting for valid commands

#### Scenario: Shutdown via stdin EOF
- **WHEN** testaruda closes the adapter's stdin
- **THEN** the adapter releases all resources (open file handles, any locks), waits for in-flight commands to complete, and exits with code 0

#### Scenario: Existing commands unaffected
- **WHEN** `titi testaruda-adapter` has never been invoked
- **THEN** `titi affected`, `titi test-manifest`, `titi build-manifest`, `titi open`, `titi pkg`, `titi check`, `titi audit`, `titi version`, `titi bundle`, and `titi repl` all behave identically to their pre-adapter specification
