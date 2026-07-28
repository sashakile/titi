## ADDED Requirements

### Requirement: Test Item Discovery via VSTest (TD-01)

The system SHALL enumerate individual test items from .NET test projects by invoking `dotnet test --list-tests` on each test project's `.csproj`, parsing the output to extract test method identities.

> **VSTest output format (verified against .NET 10 SDK 10.0.302):** `dotnet test --list-tests` produces **plain console text** by default — there is no JSON output mode for `--list-tests`. The `--report-json`, `--report-trx`, and `--logger` flags all error out (`MSB1001: Unknown switch`) when combined with `--list-tests`. The console output is: optional MSBuild preamble lines, a `"The following Tests are available:"` header, then one indented fully-qualified test name per line, with parameterized rows expanded as `Namespace.Class.Method(param: value)`. The system SHALL parse this console format. If structured JSON output ever becomes available for `--list-tests`, the system SHALL auto-detect JSON-vs-console (leading `{`) and parse accordingly.
>
> **Framework detection:** The console `--list-tests` output does NOT expose the test framework's executor URI, so the framework CANNOT be reliably detected from `--list-tests` output alone. Discovered items SHALL default to `:xunit` (titi's primary target framework) when parsing console output. (Framework may be refined later from TRX run results, which DO include the executor URI.) Unrecognised frameworks from JSON/TRX sources SHALL be classified as `:unknown` and still enumerated.
>
> **Parameterized tests:** xUnit `[Theory]`, NUnit `[TestCase]`, and MSTest `[DataTestMethod]` each produce multiple test items (one per data row). The system SHALL enumerate each data row as a separate `TestItem`, preserving the serialized arguments in the `TestId` (e.g. `Namespace.Class.Method(input: "a")`) while splitting `ClassName` and `MethodName` from the unparameterized head. If a parameterized test source (e.g., `[MemberData]`) returns zero rows, the method SHALL produce zero `TestItem` entries but remain eligible for project-level selection. The system SHALL emit a warn-level diagnostic if a single method produces more than 1000 test cases.

#### Scenario: Successful enumeration on .NET 10
- **WHEN** `dotnet test --list-tests` enumerates `Orion.Core.Tests` against an xUnit test project with 5 `[Fact]` methods
- **THEN** 5 `TestItem` records are returned, each with framework `:xunit`, parsed from the default console text output (JSON output is not available for `--list-tests` on .NET 10)

#### Scenario: No tests in project
- **WHEN** `dotnet test --list-tests` returns no test methods for a given project
- **THEN** an empty vector is returned

#### Scenario: VSTest failure
- **WHEN** `dotnet test --list-tests` fails (non-zero exit, build failure, or missing project)
- **THEN** the system emits error E011 (TEST_FAILED) with the underlying error text

#### Scenario: Cache hit
- **GIVEN** the test project's `.csproj` and test source files have not changed since the last enumeration
- **WHEN** `titi tests list` is invoked
- **THEN** cached `TestItem` records are returned without invoking `dotnet test`

#### Scenario: Parameterized test with data rows
- **WHEN** `dotnet test --list-tests` enumerates an xUnit `[Theory]` with 3 data rows: `Parse("a")`, `Parse("b")`, `Parse("c")`
- **THEN** 3 `TestItem` records are created, one per data row, with methodName `Parse("a")`, `Parse("b")`, `Parse("c")` respectively, all sharing the same className and sourceFile

#### Scenario: Parameterized test with zero rows (MemberData returning empty)
- **WHEN** `dotnet test --list-tests` enumerates a `[Theory]` backed by `[MemberData]` that returns zero data rows
- **THEN** zero `TestItem` records are created for that method, and the method is added to the project-level fallback set (selected if its project is affected)

#### Scenario: Large parameterized test warning
- **GIVEN** a `[Theory]` with 1500 data rows
- **WHEN** `titi tests list` is invoked
- **THEN** 1500 `TestItem` records are created, a warn-level diagnostic suggesting per-case selection cost is emitted, and processing continues without error

### Requirement: Test Results Ingestion via TRX (TD-02)

The system SHALL parse Visual Studio Test Results (TRX) files produced by `dotnet test --logger trx` to extract per-test outcomes (pass/fail/skip), durations, and error messages.

#### Scenario: Successful TRX parse
- **GIVEN** a TRX file with 10 passing tests and 1 failing test
- **WHEN** `titi tests ingest results.trx` is invoked
- **THEN** 11 per-test results are returned, with the failing test annotated with its error message

#### Scenario: Malformed TRX
- **WHEN** an unparseable or invalid TRX file is provided
- **THEN** the parser emits a warn-level diagnostic and returns an empty result set; the edge cache is not modified. When this occurs during the explicit `titi tests ingest` command, the command exits with code 1 (the user-requested ingestion failed). When this occurs during internal ingestion as part of selection (e.g. `titi test-manifest --select`), the system falls back to project-level selection without aborting the command.

#### Scenario: Empty TRX (no tests ran)
- **GIVEN** a TRX file with zero test results (e.g. a project with no matching tests)
- **WHEN** `titi tests ingest` is invoked
- **THEN** an empty result set is returned with no error

### Requirement: Coverage-Based Test-to-Source Edge Construction (TD-03)

The system SHALL parse Cobertura XML coverage files produced by `dotnet test --collect "XPlat Code Coverage"` to construct `TestToSourceEdge` instances, mapping each test method to the source files it exercises.

> **Granularity limitation:** Cobertura XML from `dotnet test --collect "XPlat Code Coverage"` (via Coverlet) reports coverage at the **source-file** level for the entire test run, not per test method. The correlation between which test covered which source file is approximate: the system creates one edge from each test to each source file covered during the run. This produces an over-approximation (a test may be linked to a source file it never directly exercised, if another test in the same run did). This is acceptable for the over-approximation invariant (SAFE-001) and is consistent with testaruda's approach (file-level granularity when `symbol_model_complete` is false).
>
> **Known limitation:** Coverlet on .NET 10 may attribute source-file hits to the wrong test method or fail to attribute them at all in certain configurations (dotnet/coverlet#1654). If method-level attribution is unreliable during validation (Task 7.3), the system SHALL fall back to test-project-level edges: every test in a project depends on every source file that the project's tests collectively cover.
>
> **Edge construction process:** (1) Parse TRX to get per-test `testId` and outcome. (2) Parse Cobertura to get the set of source files covered during the run. (3) Create one `TestToSourceEdge` per (test, source-file) pair with origin `:static` and weight `1_000_000`. (4) Persist edges to `$(testDetection.cacheDir)/edges/` (default `.titi/test-cache/edges/`, see `configuration` spec CF-07).

#### Scenario: Edge construction from coverage
- **GIVEN** a Cobertura report showing that 3 source files were covered during a run of 5 tests
- **WHEN** the coverage report is ingested alongside a TRX file
- **THEN** 15 `TestToSourceEdge` instances are constructed (5 tests × 3 source files), each with origin `:static` and weight `1_000_000`

#### Scenario: No coverage available
- **WHEN** no coverage data is available for a test run
- **THEN** no edges are constructed, and the system falls back to project-level selection for affected tiers

#### Scenario: Partial coverage
- **GIVEN** a Cobertura report that covers 2 of 5 test files (but all 5 tests ran)
- **WHEN** edges are constructed
- **THEN** edges are created from each of the 5 tests to the 2 covered source files; the 3 uncovered source files have no edges and changes to them trigger project-level fallback

#### Scenario: Skipped tests receive no edges
- **GIVEN** a TRX file reports 2 tests as `:skipped` and 3 as `:passed`, and a Cobertura report lists 4 covered source files
- **WHEN** edges are constructed
- **THEN** edges are created only from the 3 `:passed` tests to the 4 source files (12 edges); the 2 `:skipped` tests receive no edges because they did not execute during the run

### Requirement: Test Selection with Safety Invariants (TD-04)

The system SHALL compute the selected test set as the union of:
1. Tests reachable via `TestToSourceEdge` from changed files
2. The always-run set (tests that failed last run, newly added, with no history, quarantined, or matching must-run rules)

The system SHALL fall back to project-level selection (select all tests in an affected test project) when:
- Confidence falls below the configured threshold
- One or more changed files cannot be resolved against known test-to-source edges
- An adapter failure occurs (testaruda adapter mode only)

> **Invariant — Over-approximation (SAFE-001):** The computed selected set SHALL be a superset of the true affected set. Any test that *could* be affected by a change SHALL be selected. This is the soundness invariant.

#### Scenario: Direct dependency selects test
- **GIVEN** `TestParser` has a `TestToSourceEdge` to `src/Parser.cs` and `Parser.cs` is in the change set
- **WHEN** selection is computed
- **THEN** `TestParser` is selected

#### Scenario: Always-run includes newly discovered test
- **GIVEN** a `TestItem` was discovered from `dotnet test --list-tests` and no prior run history exists for it
- **WHEN** selection is computed
- **THEN** the test is included via the always-run set with reason `:newly-added`

#### Scenario: Fallback on unresolved file
- **GIVEN** a changed file `src/config.toml` has no `TestToSourceEdge` from any test
- **WHEN** selection is computed
- **THEN** all tests in any test project whose MSBuild graph includes `src/config.toml` are selected (project-level fallback)

#### Scenario: Confidence below threshold
- **GIVEN** 3 of 10 changed files have no matching `TestToSourceEdge`, yielding confidence 0.7
- **WHEN** the configured `fallbackThreshold` is 0.8
- **THEN** the system falls back to project-level selection for the affected test projects and emits exit code 10

### Requirement: Always-Run Set Eviction (TD-05)

The system SHALL track consecutive passing runs for each test item. A test SHALL be evicted from the always-run set after `alwaysRunEvictionThreshold` consecutive passing runs (default: 5).

#### Scenario: Test evicted after consecutive passes
- **GIVEN** a test was in the always-run set due to a prior failure
- **WHEN** it passes 5 consecutive runs
- **THEN** it is removed from the always-run set and will only be selected via test-to-source edges going forward

#### Scenario: Failure resets counter
- **GIVEN** a test has 4 consecutive passes
- **WHEN** it fails on the 5th run
- **THEN** the consecutive pass counter resets to 0 and the test re-enters the always-run set

### Requirement: Run History Persistence (TD-06)

The system SHALL persist per-test run outcomes (pass/fail, duration, timestamp) to `$(testDetection.cacheDir)/history.edn` (default `.titi/test-cache/history.edn`, see `configuration` spec CF-07), enabling cross-session always-run set computation.

> **Serialization format:** The history file SHALL be EDN (`ext:edn`), consistent with titi's ClojureCLR implementation and `titi.config.edn`. Each entry is a map with keys `:test-id`, `:outcome` (`:passed`/`:failed`/`:skipped`), `:duration-ms` (number), and `:timestamp` (ISO-8601). The top-level structure is a map of `test-id` → vector of entries (most-recent-last). This replaces an earlier `.jls` (Julia serialization) proposal that was incompatible with titi's runtime.

> **Retention policy:** To prevent unbounded file growth, the system SHALL retain at most the last 100 run entries per test. The oldest entries are evicted when a new entry pushes the count past this threshold. Additionally, the history file SHALL be compacted (cleaned of evicted entries) whenever its size exceeds 10 MB.

#### Scenario: History recorded
- **WHEN** `titi tests ingest` processes a TRX file with 3 test results
- **THEN** 3 run-history entries are written to `$(testDetection.cacheDir)/history.edn`, each with test-id, outcome, duration, and timestamp

#### Scenario: History loaded
- **WHEN** a subsequent `titi affected` or `titi test-manifest --select` invocation runs
- **THEN** the persisted run history is loaded to populate the always-run set

#### Scenario: No history file
- **WHEN** `$(testDetection.cacheDir)/history.edn` does not exist
- **THEN** the system treats all discovered tests as having no history (all added to always-run set)

#### Scenario: Retention eviction
- **GIVEN** a test has 101 recorded runs in history
- **WHEN** a new entry is added for that test
- **THEN** the oldest entry is evicted, keeping the count at 100

#### Scenario: Compaction on oversized file
- **GIVEN** the history file exceeds 10 MB and contains stale entries from evicted tests
- **WHEN** a new write is triggered
- **THEN** the file is compacted: only retained entries are written, and the file size drops below 10 MB

### Requirement: Missed-Selection Incident Tracking (TD-07)

The system SHALL compare the selected test set against full-run outcomes after each full regression, recording `MissedSelectionIncident` entries for any test that failed in the full run but was NOT in the selected set.

#### Scenario: Incident recorded for missed failure
- **WHEN** a full run reveals `TestParser` failed, but `TestParser` was not in the selected set for the change
- **THEN** a `MissedSelectionIncident` is recorded with status `:candidate`

#### Scenario: Incident promoted after 3 occurrences
- **GIVEN** the same `(changed-content, missed-test)` pair has been observed 3 times
- **WHEN** incidents are processed after the 3rd occurrence
- **THEN** the incident is promoted to `:promoted`, creating a permanent `:manual` edge that forces selection on that change path
