## 1. Domain types
- [x] 1.1 Define `TestItem` record: test-id, assembly-path, class-name, method-name, framework (:xunit | :nunit | :mstest), tier, source-file, last-outcome, mean-duration-ms, tags
- [x] 1.2 Define `TestToSourceEdge` record: from (test-id), to (source-file-path), origin (:static | :runtime | :manual), weight (ppm confidence), line-ranges
- [x] 1.3 Define `AlwaysRunReason` enum: LAST-RUN-FAILED, NEWLY-ADDED, NO-HISTORY, MUST-RUN, QUARANTINED
- [x] 1.4 Define `FallbackReason` enum: CONFIDENCE-BELOW-THRESHOLD, UNRESOLVED-FILE, ADAPTER-FAILURE, ENVIRONMENT-CHANGE
- [x] 1.5 Define `MissedSelectionIncident` record: changed-content, missed-test-id, timestamp, status (candidate | promoted | dismissed)
- [x] 1.6 Define `TestSelectionResult` record: test-id, selected?, reasons (vector of reason-kind + description chains), confidence, fallback-reason
- [x] 1.7 Modify `TieredTestSet`: add `items` field (map of tier → vector of TestItem); `unit`, `integration`, etc. now contain both projects and items
- [x] 1.8 Add `selectedTests` field to `AffectedSet`

## 2. Test discovery via VSTest
- [x] 2.1 Implement `dotnet test --list-tests` invocation for a given .csproj (on .NET 10, **console text** output — no JSON mode exists for `--list-tests`; auto-detect JSON-vs-console in the parser)
- [x] 2.2 Parse VSTest `--list-tests` output into `TestItem` records (xUnit/NUnit/MSTest format detection)
- [ ] 2.3 Handle parameterized tests: one TestItem per data row; zero-row MemberData produces zero items with project-level fallback; warn if >1000 cases per method
- [ ] 2.4 Implement `discover_test_items(project-path)` — returns vector of TestItem for one project
- [ ] 2.5 Implement `discover_test_items(repo)` — enumerates across all test projects in graph, grouped by tier
- [ ] 2.6 Cache test-item lists in `.titi/test-cache/items/`, invalidated when `.csproj` or test source files change
- [ ] 2.7 Handle `--list-tests` errors: dotnet not found → E007; project has no tests → empty list; malformed output → warn + empty
- [ ] 2.8 Validate VSTest output parsing against real (not synthetic) xUnit, NUnit, and MSTest projects with nested classes, generics, and parameterized tests — create these fixtures in addition to the synthetic monorepo fixture

## 3. Test-to-source dependency edges
- [x] 3.1 Implement Cobertura XML coverage parser for `dotnet test --collect "XPlat Code Coverage"` output
- [x] 3.2 Implement `build_edges_from_coverage(cobertura-xml)`  (EdgeBuilder.BuildFromRun) → vector of TestToSourceEdge (one per test×source-file pair)
- [x] 3.3 Implement `build_edges_from_trx(trx-path)` → per-test duration and outcome  (Coverage.Parser.ParseTrx → TrxTestResult[]; per-test outcome + duration + error, TD-02)
- [x] 3.4 Wire coverage and TRX together: invoke tests once, collect both outputs, build edge set  (EdgeBuilder.BuildFromRun(trxResults, coveredSources); file-level cross-product, From=testName To=sourceFile, Origin=Static weight=1_000_000; skips NotExecuted tests)
- [ ] 3.5 Store test-to-source edges in `.titi/test-cache/edges/` with source file fingerprints
- [ ] 3.6 Incremental edge update: only re-run tests whose source-fingerprint changed
- [ ] 3.7 Fallback when no coverage: treat all test items in an affected project as selected (project-level fallback, matching current behavior)
- [ ] 3.8 Verify Cobertura method-level coverage attribution against .NET 10 fixture before enabling per-test edge construction. If method-level attribution is unreliable, fall back to test-project-level edges (every test in a project depends on all source files the project's tests collectively cover).

## 4. Safety invariants and selection logic
- [ ] 4.1 Implement `compute_always_run_set(discovered-items, run-history)` — returns set of test-ids that must run; "newly added" is relative to last recording, not last discovery
- [ ] 4.2 Implement `compute_selected_tests(changed-files, test-to-source-edges, always-run-set)` — returns vector of TestSelectionResult with reason chains
- [ ] 4.3 Implement confidence scoring: ratio of resolved changed-files to total changed-files, coverage freshness, history depth
- [ ] 4.4 Implement fallback logic: if confidence below threshold, fall back to project-level selection for affected test projects (select all tests in the project)
- [ ] 4.5 Implement missed-selection incident recording and promotion
- [x] 4.6 Persist run history in `.titi/test-cache/history.edn` (EDN format: test-id -> vector of {:test-id :outcome :duration-ms :timestamp}), with retention (max 100 entries per test) and compaction (>10 MB triggers cleanup)  (HistoryStore.AppendResults/SerializeEdn/ParseEdn/CompactIfOversized; minimal recursive-descent EDN reader for the emitted subset; wired into Ingestor.IngestRun + TestsIngestCommand + TestsRecordCommand; verified record -> history.edn, ingest appends to existing)

## 5. CLI commands
- [x] 5.1 Implement `titi tests list <project-pat>` — enumerate test items, optionally filtered by tier
- [x] 5.2 Implement `titi tests ingest <trx-path> [--coverage <cobertura-path>]` — parse results and update edge cache; TRX-Cobertura correlation produces per-test×source-file edges  (Ingestor.IngestRun -> EdgeBuilder.BuildFromRun; edges keyed From=testName To=sourceFile, written to .titi/test-cache/edges/edges.edn; malformed TRX -> exit 1 no cache modification; TRX-only -> no edges written, preserves prior index; history deferred to 4.6)
- [x] 5.3 Implement `titi tests record` — run all test projects with coverage, ingest results, build edge index (ci-performed)  (Core.TestsRecordCommand: graph->test-projects->dotnet test --collect XPlat --logger trx->ArtifactLocator->ParseTrx+ParseCobertura->EdgeBuilder->.titi/test-cache/edges/; content-based fingerprint incremental skip)
- [ ] 5.4 Upgrade `titi test-manifest` with `--select` flag: when enabled, emit per-test-filtered Traversal using `dotnet test --filter`; framework-aware filter syntax (xUnit `~`, NUnit `==`, MSTest `TestCategory` or `~`); batch splitting when filter >4000 chars
- [ ] 5.5 Upgrade `titi test-manifest` with `--list` flag: print selected test IDs instead of emitting a Traversal file
- [ ] 5.6 Upgrade `titi affected` to include `selectedTests` and `confidence` in `--output json` when test edges are available
- [ ] 5.7 Add exit code 10 (run full suite) when confidence fallback fires; exit code 20 (safe to skip) when selected tests is empty

## 6. Configuration
- [x] 6.1 Add `TestDetectionConfig` to `TitiConfig`:
  - `enabled` (boolean, default: false)
  - `vstest-path` (string, default: "dotnet")
  - `collect-coverage` (boolean, default: false — set true by `titi tests record`)
  - `coverage-format` (:cobertura | :opencover, default: :cobertura)
  - `cache-dir` (string, default: ".titi/test-cache" — root for edges/, items/, history.edn)
  - `fallback-threshold` (float [0,1], default: 0.7)
  - `always-run-eviction-threshold` (integer, default: 5)
  - `batch-size` (integer, default: 100)
  - `exclude-patterns` (string[], default: [])

## 7. Testing
- [x] 7.1 Create fixture: synthetic .NET monorepo with 2 test projects (one xUnit, one NUnit) and 3 library projects  (Orion.UnitTests xUnit, Orion.IntegrationTests NUnit, libs Orion.Core.Data/Auth/Storage; coverlet.collector added; library source files added)
- [x] 7.2 Verify `titi tests list` enumerates all test methods from fixture test projects (including parameterized, nested, and generic methods)  (18 methods enumerated: parameterized rows expanded, NestedAuthTests+Inner preserved with + syntax, FactoryTests.CreateInstance_Foo generic-method shape)
- [x] 7.3 Verify `titi tests ingest` parses fixture TRX and Cobertura output correctly; verify Cobertura attribution quality on .NET 10  (ingest parses Cobertura into edges; record produces 93 test×source edges across 25 tests / 4 library source files). NOTE: ingest currently writes method-keyed edges — TID-3c (titi-wnj) tracks routing ingest through EdgeBuilder for the spec-correct test×source cross-product. File-level Cobertura attribution confirmed reliable on .NET 10; method-level not pursued (TD-03 known limitation).
- [ ] 7.4 Verify `titi test-manifest --select` emits filtered vs unfiltered Traversal correctly; verify framework-aware filter syntax and batch splitting
- [ ] 7.5 Verify always-run set includes failed/new/no-history tests; verify "newly added" relative to recording (not discovery)
- [ ] 7.6 Verify confidence fallback triggers when below threshold
- [ ] 7.7 Verify rollback: removing test-detection module leaves existing commands functional
- [ ] 7.8 Fixture maintenance: regenerate when test-item or edge schemas change

## 8. Documentation
- [ ] 8.1 Document test-detection capability in README
- [ ] 8.2 Document `titi tests list`, `titi tests ingest`, `titi tests record` in CLI reference
- [ ] 8.3 Document safety invariants and confidence model
- [ ] 8.4 Document relation to `add-testaruda-adapter` Phase 2 (method-level granularity builds on these primitives; this change is a prerequisite, not a replacement)
