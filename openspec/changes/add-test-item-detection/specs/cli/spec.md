## MODIFIED Requirements

### Requirement: Exit Codes (CLI-18)

The system SHALL use exit code 0 for success, 1 for all command failures (including validation, graph, or build errors), 2 for usage errors (invalid arguments or unknown subcommands), 10 for confidence-fallback full-suite selection (test selection confidence fell below the configured threshold and the system recommends running the full test suite), and 20 for safe-to-skip selection (no tests were selected and the always-run set is empty — it is safe to skip the test phase entirely). Exit codes 10 and 20 SHALL only be emitted by test-selection commands (`titi test-manifest --select`, `titi affected` when test edges are available) and SHALL NOT be emitted by non-test commands.

> **Relationship to test-detection:** Exit code 10 indicates the over-approximation invariant (SAFE-001) could not be satisfied with high confidence, so the system fell back to project-level selection and recommends a full-suite run. Exit code 20 indicates the change set touches no test-reachable code and no always-run tests exist, so skipping tests is safe. See `test-detection` spec TD-04 and `cli` CLI-06 (`titi test-manifest --select`).

#### Scenario: Successful command exit
- **WHEN** any titi command completes without errors
- **THEN** the process exits with code 0

#### Scenario: Command failure exit
- **WHEN** a titi command encounters a runtime error
- **THEN** the process exits with code 1

#### Scenario: Invalid argument exit
- **WHEN** an unrecognised flag is passed to any titi command
- **THEN** the process exits with code 2 and prints usage help to stderr

#### Scenario: Confidence-fallback exit
- **GIVEN** test selection confidence falls below the configured `fallbackThreshold`
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** the process exits with code 10, a diagnostic recommends a full-suite run, and no Traversal is written

#### Scenario: Safe-to-skip exit
- **GIVEN** no tests are selected (no edges match the change set and the always-run set is empty)
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** the process exits with code 20 and no Traversal .proj is written

### Requirement: titi test-manifest (CLI-06)

The system SHALL implement `titi test-manifest` which generates a Traversal .proj scoped to affected test projects (see `dependency-graph` spec, DG-04/DG-06), organised by tier. When `--select` is provided and test-to-source edges are available, the system SHALL emit a per-test-filtered Traversal by injecting `dotnet test --filter` arguments scoped to the selected test items. When `--select` is provided but no test-to-source edges are available, the system SHALL emit a warn-level diagnostic and fall back to project-level Traversal without filtering. When `--list` is provided, the system SHALL print selected test IDs to stdout one per line instead of emitting a Traversal file.

> **Filter syntax by framework:** The `--filter` expression SHALL be generated based on the detected test framework of the target project. For xUnit, filter by `FullyQualifiedName~FQN` (substring match). For NUnit, filter by `FullyQualifiedName==FQN` (exact match, NUnit canonical format). For MSTest, filter by `TestCategory` if the test is tagged, otherwise by `FullyQualifiedName~FQN`. For mixed-framework projects (`:unknown`), use `dotnet test` per selected test (one invocation per test) as the safe default.
>
> **Filter expression derivation (testId → FQN):** VSTest's `--filter` operates on `FullyQualifiedName`, which is `<namespace.class>.<method>` (dot-separated, no assembly prefix, no `::`). A `TestItem.testId` (see `domain-model` spec DM-07) is `<assembly>::<namespace.class>::<method[(args)]>`. The filter expression SHALL be derived from `testId` by: (1) stripping the leading `<assembly>::` component, (2) replacing the remaining `::` separator with `.`, (3) percent-decoding any encoded arguments. The result is the framework's `FullyQualifiedName` form. Example: `testId` `Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseValidInput` → `FullyQualifiedName` `Orion.Core.Tests.ParserTests.ParseValidInput`.
>
> **Parameterized-test row selection fallback:** Some frameworks cannot filter to a *single data row* of a parameterized test via `FullyQualifiedName` alone (e.g. xUnit `~` substring-matches all rows of a `[Theory]`; NUnit `TestCase` row-level filtering has gaps). When a selected `TestItem` is a parameterized row that the framework cannot filter individually, the system SHALL select the entire method (all rows) by filtering on the method's `FullyQualifiedName` without the argument suffix, emit a warn-level diagnostic noting the over-approximation, and proceed. This is consistent with the over-approximation invariant (SAFE-001, see `test-detection` spec TD-04): selecting more tests than strictly necessary is safe; selecting fewer is not.
>
> **Filter length safety:** If the generated `--filter` string exceeds 4000 characters, the system SHALL split the test set into batches and generate multiple `dotnet test` invocation entries in the Traversal. The default batch size SHALL be 100 tests per batch, configurable via `TestDetectionConfig.batchSize`. Batches may be run in parallel by the CI executor (Traversal SDK handles parallel execution of `<ProjectReference>` items).

#### Scenario: Per-test filtered manifest
- **GIVEN** an affected xUnit test project `Orion.Core.Tests` with 10 discovered test items, of which 3 are selected (non-parameterized `[Fact]` methods)
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** the emitted Traversal .proj references `Orion.Core.Tests.csproj` with a `--filter` argument of the form `FullyQualifiedName~Orion.Core.Tests.ParserTests.ParseValidInput|…` (one derived FQN per selected test, joined by `|`), and only the 3 selected tests run

#### Scenario: testId is transformed to FullyQualifiedName for the filter
- **GIVEN** a selected `TestItem` with `testId` `Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseValidInput`
- **WHEN** the `--filter` expression is generated for an xUnit project
- **THEN** the filter uses `FullyQualifiedName~Orion.Core.Tests.ParserTests.ParseValidInput` (assembly prefix stripped, `::` replaced with `.`, no `::` in the filter value)

#### Scenario: Parameterized row falls back to whole-method selection
- **GIVEN** a selected `TestItem` is a parameterized row `Orion.Core.Tests::Orion.Core.Tests.ParserTests::Parse("a")` in an xUnit project, and xUnit `FullyQualifiedName~` cannot select a single data row
- **WHEN** the `--filter` expression is generated
- **THEN** the filter targets the whole method `FullyQualifiedName~Orion.Core.Tests.ParserTests.Parse` (argument suffix dropped), all rows of `Parse` run, and a warn-level diagnostic notes the over-approximation

#### Scenario: NUnit project uses exact-match filter
- **GIVEN** an affected NUnit test project with 3 selected non-parameterized test items
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** the emitted filter uses `FullyQualifiedName==<derived-FQN>` syntax (exact match) for each selected test

#### Scenario: Unfiltered manifest (no --select)
- **GIVEN** an affected test project `Orion.Core.Tests`
- **WHEN** `titi test-manifest` is invoked without `--select`
- **THEN** the emitted Traversal .proj references `Orion.Core.Tests.csproj` without any `--filter` argument, matching current behaviour (all tests run)

#### Scenario: --list flag emits test IDs
- **GIVEN** 3 selected test items
- **WHEN** `titi test-manifest --select --list` is invoked
- **THEN** 3 test IDs are printed to stdout, one per line, and no Traversal file is emitted

#### Scenario: Fallback when no edges available
- **GIVEN** the user passes `--select` but no `TestToSourceEdge` cache exists
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** a warn-level diagnostic is emitted, and the Traversal .proj falls back to project-level (no filter), running all tests in affected projects

#### Scenario: Exit code 10 on confidence fallback
- **GIVEN** confidence is below threshold when `--select` is used
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** exit code is 10, the diagnostic recommends full suite run, and no Traversal is written

#### Scenario: Exit code 20 on empty selection
- **GIVEN** no tests are selected (no edges match and always-run set is empty)
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** exit code is 20, no Traversal .proj is written

#### Scenario: Filter exceeds length limit, split into batches
- **GIVEN** 5000 selected tests produce a `--filter` string of 48000 characters
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** the Traversal .proj emits 50 `<ProjectReference>` items (at 100 tests per batch default), each with a distinct `--filter` argument scoped to its batch

## ADDED Requirements

### Requirement: titi tests list (CLI-20)

The system SHALL implement `titi tests list [project-pat]` which enumerates individual test items from the specified test project(s), displaying each test's test ID, class name, method name, framework, and tier. When no project pattern is provided, all test projects in the graph are enumerated. Output is text by default (one test per line) and JSON when `--output json` is used.

#### Scenario: List all tests
- **GIVEN** a monorepo with 2 test projects containing 15 total test methods
- **WHEN** `titi tests list` is invoked
- **THEN** 15 test items are printed to stdout, one per line, each showing test ID and framework

#### Scenario: List tests filtered by project pattern
- **WHEN** `titi tests list Orion.Core` is invoked
- **THEN** only test items from projects matching `*Orion.Core*` are enumerated

#### Scenario: List tests JSON output
- **WHEN** `titi tests list --output json` is invoked
- **THEN** a JSON array of `TestItem` objects is printed to stdout

#### Scenario: No test projects
- **WHEN** `titi tests list` is invoked on a monorepo with no test projects
- **THEN** no output is produced and exit code is 0

### Requirement: titi tests ingest (CLI-21)

The system SHALL implement `titi tests ingest <trx-path> [--coverage cobertura-path]` which parses a TRX test results file and optional Cobertura coverage file to update the test-to-source edge cache and run history. TRX-only invocation updates run history (outcomes, durations). TRX + coverage invocation also builds `TestToSourceEdge` instances.

> **TRX-Cobertura correlation:** Test results from TRX are keyed by `testName` (VSTest's `FullyQualifiedName`). Cobertura XML reports coverage at the **source-file** granularity, not per-test-method. To build per-test edges from these two inputs, the system SHALL: (1) parse TRX to get per-test outcomes and durations; (2) parse Cobertura to get the set of source files covered during the entire run; (3) create a single `TestToSourceEdge` from each test to each covered source file (file-level granularity). Method-level line-range edges are a future enhancement when Cobertura provides reliable per-method attribution on .NET 10. See also the known limitation documented in TD-03.

#### Scenario: Ingest with TRX and coverage
- **GIVEN** a TRX file with 5 test results and a Cobertura XML file listing 3 source files as covered during the run
- **WHEN** `titi tests ingest results.trx --coverage coverage.cobertura.xml` is invoked
- **THEN** 5 run-history entries are written and 15 `TestToSourceEdge` entries are built (5 tests × 3 source files), written to `$(testDetection.cacheDir)/edges/` (edges) and `$(testDetection.cacheDir)/history.edn` (history)

#### Scenario: Ingest TRX only
- **WHEN** `titi tests ingest results.trx` is invoked without `--coverage`
- **THEN** run history is updated (5 outcomes, durations) but no edges are built

#### Scenario: Ingest with malformed input
- **WHEN** an unparseable TRX file is provided
- **THEN** exit code is 1, a warn-level diagnostic is emitted, and the edge cache is not modified

### Requirement: titi tests record (CLI-22)

The system SHALL implement `titi tests record` which runs all test projects with coverage collection enabled, ingests the resulting TRX and Cobertura output, and builds the complete test-to-source edge index.

#### Scenario: Full recording
- **GIVEN** a monorepo with 3 test projects
- **WHEN** `titi tests record` is invoked
- **THEN** all 3 test projects are built and run with `--collect "XPlat Code Coverage" --logger trx`, results are ingested, and `$(testDetection.cacheDir)/edges/` is populated

#### Scenario: Incremental recording
- **GIVEN** `$(testDetection.cacheDir)/edges/` exists and source fingerprints are unchanged
- **WHEN** `titi tests record` is invoked
- **THEN** no tests are re-run (cache is fresh), and exit code is 0

#### Scenario: Recording on first invocation
- **GIVEN** no `$(testDetection.cacheDir)/edges/` exists
- **WHEN** `titi tests record` is invoked
- **THEN** all test projects are run from scratch and the edge cache is built
