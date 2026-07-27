# Change: Add test-item-level detection and test-to-source dependency mapping

## Why

titi's current test-impact analysis operates at the **project level**: `titi test-manifest` emits whole `.csproj` files as test units, and `AffectedSet` maps changed source projects to affected test projects. This is equivalent to "any change in this repo → run all tests in downstream test assemblies," which provides minimal CI savings.

testaruda (language-agnostic TIA) and Testimonial.jl (Julia-native TIA) demonstrate a finer-grained pattern:
- **Test-item-level detection**: individual test methods (`@testitem`, `[Fact]`) are enumerated and selected independently
- **Test-to-source dependency edges**: each test records which source lines/files it exercises, creating a per-test dependency graph
- **Coverage feedback**: runtime test results feed back into the dependency model, refining edges for future selections
- **Safety invariants**: always-run sets (failed/new tests), confidence thresholds, fallback to full suite

This change adds those capabilities to titi's **core** — independent of the testaruda adapter. The adapter's deferred Phase 2 (method-level granularity) will build on these primitives rather than re-inventing them.

## What Changes

- **New `TestItem` domain schema**: individual test methods identified by assembly, class, name, and framework (xUnit/NUnit/MSTest)
- **New `test-detection` capability**: VSTest `--list-tests` integration for enumerating test items from .NET test projects; TRX output parsing for per-test results; Cobertura XML coverage parsing for test-to-source edge construction
- **New `TestToSourceEdge` type**: dependency edges from individual tests to source files, annotated with origin (static via coverage, manual) and confidence weight
- **New safety-invariant types**: `AlwaysRunReason` enum, `FallbackReason` enum, `MissedSelectionIncident` — ported from testaruda's safety model
- **Upgraded `TieredTestSet`**: now contains `TestItem[]` in addition to `ProjectDescriptor[]`, enabling per-test selection
- **Upgraded `AffectedSet`**: new `selectedTests` field with per-test selection results, reason chains
- **Upgraded `titi test-manifest`**: new `--select` flag that emits a per-test filtered Traversal (not just per-project)
- **New `titi tests list` command**: enumerate test items in a project without running them
- **New `titi tests ingest` command**: ingest TRX + Cobertura results to build/update test-to-source dependency edges
- **Extended `TitiConfig`**: new `TestDetectionConfig` section for VSTest path, exclude patterns, coverage preferences

## Design References

Borrowed patterns from referenced projects:

| Pattern | Source | Adaptation |
|---------|--------|------------|
| Safety invariant (over-approximation), always-run set | testaruda TIA-SAFE-001/007 | TD-04 — same semantics, scoped to titi's project/tier model |
| Always-run eviction threshold | Testimonial.jl `DEFAULT_ALWAYS_RUN_EVICTION_THRESHOLD` (5 consecutive passes) | TD-05, CF-07 — same default, configurable |
| Semiring weight convention (1_000_000 = full confidence) | testaruda TIA-ADAPT-009 | DM-08 — same ppm scale |
| Missed-selection incident lifecycle | testaruda TIA-SAFE-008 | DM-11, TD-07 — simplified to candidate→promoted two-state model |
| Run history persistence | Testimonial.jl `RunHistory` + `save_run_history` | TD-06 — similar serialization, titi-specific path |

## Coordination with `add-testaruda-adapter`

This change and `add-testaruda-adapter` are **independent at implementation time**:
- `add-testaruda-adapter` operates at project granularity (`isTestProject`), requiring no test-item primitives
- `add-test-item-detection` adds the core types (`TestItem`, `TestToSourceEdge`, `TestSelectionResult`) as a new capability

When `add-testaruda-adapter`'s deferred Phase 2 (method-level granularity) is implemented, it will consume these core types rather than re-implementing them. This change is therefore a **prerequisite** for that Phase 2, not a replacement for it.

## Impact

- Affected specs: `domain-model` (new types, modified `TieredTestSet`), `dependency-graph` (new `selectedTests` on `AffectedSet`, new `TestToSourceEdge`), `cli` (upgraded `test-manifest`, new `tests list/ingest` commands, extended exit codes 10/20 via MODIFIED CLI-18), `configuration` (new `testDetection` section), `diagnostics` (un-reserve E011 for VSTest failures via MODIFIED DX-02), new `test-detection` spec
- Affected code: new `src/titi/test_detection/` module; modified `src/titi/graph.clj` for test-to-source edges; modified `src/titi/cli.clj` for new/upgraded commands
- Dependencies (optional): `dotnet` CLI for VSTest listing; `--collect "XPlat Code Coverage"` for Cobertura coverage; no new NuGet packages
- **Not affected**: `titi open`, `titi pkg`, `titi check`, `titi audit`, `titi version`, `titi bundle`, `titi repl` — unchanged
- **Relation to `add-testaruda-adapter`**: this change provides the core test-detection primitives that the adapter's deferred Phase 2 will consume. The two changes are compatible and additive; the adapter can continue at project granularity while these primitives are built.
