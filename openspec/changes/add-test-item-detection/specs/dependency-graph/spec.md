## MODIFIED Requirements

### Requirement: AffectedSet and TieredTestSet Schemas (DG-06)

The system SHALL define the `AffectedSet` schema with fields: `changedFiles` (string[]), `directlyAffected` (ProjectDescriptor[]), `transitivelyAffected` (ProjectDescriptor[]), `affectedTests` (TieredTestSet), and `selectedTests` (TestSelectionResult[]). The system SHALL define the `TieredTestSet` schema with fields: `unit` (ProjectDescriptor[]), `package` (ProjectDescriptor[]), `integration` (ProjectDescriptor[]), `compatibility` (ProjectDescriptor[]), and `items` (map of tier → TestItem[]).

> **Invariant — Selected tests subset:** `selectedTests` SHALL be a subset of the union of all test items in `affectedTests.items`. No test SHALL be selected that was not first placed in an affected test project's tier.
>
> **Invariant — Project presence:** When `affectedTests.items` is non-empty for a tier, the corresponding `affectedTests.<tier>` (ProjectDescriptor[]) MAY still be non-empty — test items from partially-affected projects coexist with fully-affected project entries for projects with no test-item coverage data.

#### Scenario: Selected tests populated from edges
- **GIVEN** a change set affecting source file `src/Parser.cs`, and a test-to-source edge linking `TestParser` to `Parser.cs`
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** the returned `AffectedSet` has `selectedTests` containing a `TestSelectionResult` for `TestParser` with `selected: true`

#### Scenario: Selected tests with always-run inclusion
- **GIVEN** a change set that does not affect any source file linked to `TestParser`, but `TestParser` failed in its last run
- **WHEN** `titi test-manifest --select` is invoked
- **THEN** `selectedTests` contains a result for `TestParser` with `selected: true` and reason `:always-run` with description `:last-run-failed`

#### Scenario: Test items populated by tier
- **GIVEN** `discover_test_items` has run and discovered 10 xUnit tests in a unit-tier project
- **WHEN** `AffectedSet.affectedTests` is constructed
- **THEN** `items` contains `{:unit [10 TestItems]}`

#### Scenario: No test items without prior discovery
- **GIVEN** `discover_test_items` has never been invoked
- **WHEN** `AffectedSet.affectedTests` is constructed
- **THEN** `items` is empty, and the system falls back to project-level selection (all test projects in affected tiers)

## ADDED Requirements

### Requirement: Test-to-Source Edge Storage (DG-10)

The system SHALL persist `TestToSourceEdge` instances in `$(testDetection.cacheDir)/edges/` (default `.titi/test-cache/edges/`, see `configuration` spec CF-07), indexed by source file path for efficient change-set lookups. The cache SHALL be invalidated when a source file's content fingerprint changes (DG-07).

> **Cache invalidation trade-off:** The current invalidation rule (drop all edges for a changed source file) is conservative: any change to a source file — including cosmetic or formatting-only changes — invalidates all edges pointing to it. This ensures the over-approximation invariant (SAFE-001) but may cause unnecessary re-recording on PRs touching heavily-shared files (e.g., a common utility module depended on by many tests). For v1 this is acceptable; a future optimization could use per-line or per-symbol granularity to narrow invalidation scope.

#### Scenario: Edge lookup by source file
- **GIVEN** a `TestToSourceEdge` links `TestParser` to `src/Parser.cs`
- **WHEN** `src/Parser.cs` is in the change set and test selection is computed
- **THEN** the edge cache returns `TestParser` as a candidate test

#### Scenario: Cache invalidation on fingerprint change
- **GIVEN** `src/Parser.cs` has a stored fingerprint in `MonorepoGraph.fingerprints`
- **WHEN** `src/Parser.cs` content changes (new SHA-256)
- **THEN** all `TestToSourceEdge` entries referencing `src/Parser.cs` are invalidated, requiring re-discovery on next test run

#### Scenario: No edges cached
- **GIVEN** `$(testDetection.cacheDir)/edges/` does not exist or is empty
- **WHEN** test selection is computed
- **THEN** the system falls back to project-level selection (all test projects in affected tiers), no re-ingestion error is emitted

### Requirement: Selected Test Result Schema (DG-11)

The system SHALL define the `TestSelectionResult` schema with fields: `testId` (string), `selected` (boolean), `reasons` (vector of `{:kind reason-kind, :description string}`), `confidence` (float [0,1]), and `fallbackReason` (string or nil).

#### Scenario: Single direct-change reason
- **WHEN** `TestParser` is selected because `src/Parser.cs` is in the change set
- **THEN** `TestSelectionResult` has `selected: true`, `reasons: [{:kind :direct-change, :description "src/Parser.cs changed"}]`

#### Scenario: Multiple reasons for selection
- **WHEN** `TestParser` is selected because `src/Parser.cs` changed AND it failed last run
- **THEN** `reasons` contains two entries: one `:direct-change` and one `:always-run`

#### Scenario: Test excluded with reason
- **WHEN** a test is not affected by the change set and is not in the always-run set
- **THEN** `TestSelectionResult` has `selected: false` and `reasons: [{:kind :unresolved, :description "no edge from changed files"}]`
