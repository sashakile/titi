# Design: Add test-item-level detection and test-to-source dependency mapping

## Context

titi's Phase 1 test-impact analysis operates at project granularity — `titi test-manifest` emits whole `.csproj` files as test units. This provides minimal CI savings on large monorepos. testaruda (language-agnostic TIA) and Testimonial.jl (Julia-native TIA) demonstrate a finer-grained pattern: per-test-method selection backed by test-to-source dependency edges, coverage feedback, and safety invariants that over-approximate the true affected set.

This change ports that pattern to titi's core, independent of the testaruda adapter. The adapter's deferred Phase 2 (method-level granularity) will consume these primitives rather than re-inventing them.

## Goals / Non-Goals

**Goals:**
- Per-test-method discovery via VSTest `--list-tests`.
- Test-to-source dependency edges from Cobertura coverage + TRX results.
- Safety invariants (always-run set, confidence fallback) that preserve an over-approximation guarantee.
- Backward compatibility: when no edges exist, behaviour degenerates to Phase 1 project-level selection.

**Non-Goals:**
- Method-level *line-range* attribution from Cobertura (blocked by Coverlet reliability — see Decisions).
- Replacing `titi affected` / `titi test-manifest` project-level behaviour.
- The testaruda adapter itself (separate change `add-testaruda-adapter`).

## Decisions

### Decision 1: Over-approximation invariant (SAFE-001) is the soundness contract

The selected test set SHALL be a superset of the true affected set. Any test that *could* be affected by a change SHALL be selected. This is ported from testaruda TIA-SAFE-001 and is the invariant that makes test selection safe to use in CI: a false negative (missed affected test) is a correctness bug; a false positive (extra test run) is only a performance cost.

- **Rationale:** CI trust depends on never silently skipping an affected test. The cost of over-selection is bounded (project-level fallback); the cost of under-selection is a missed regression.
- **Alternatives considered:** Exact (sound and complete) selection — rejected as unattainable without full symbolic execution of the dependency graph.

### Decision 2: Cobertura method-level attribution is not relied upon; file-level edges with project-level fallback

Cobertura XML from `dotnet test --collect "XPlat Code Coverage"` (via Coverlet) reports coverage at the **source-file** level for the entire test run, not per test method. Coverlet on .NET 10 may attribute source-file hits to the wrong test method or fail to attribute them at all (dotnet/coverlet#1654).

- **Decision:** Construct edges at file granularity (one edge per (test, covered-source-file) pair, origin `:static`, weight `1_000_000`). This is an over-approximation (a test may be linked to a source file it never directly exercised) consistent with SAFE-001. If method-level attribution is unreliable during validation (Task 7.3), fall back to **test-project-level edges**: every test in a project depends on every source file that the project's tests collectively cover.
- **Alternatives considered:** (1) Wait for reliable per-method Cobertura attribution — rejected; blocks the whole change on an external tool fix. (2) Use Coverlet's `--format opencover` — same attribution limitation. (3) Use a different coverage tool — out of scope; titi targets the .NET SDK-bundled collector.
- **Risk:** Over-approximation inflates the selected set when many tests share a covered file. Accepted for v1; a future per-line/per-symbol optimisation could narrow it.

### Decision 3: Consolidated cache layout under `testDetection.cacheDir`

All test-detection caches live under a single configurable root (`testDetection.cacheDir`, default `.titi/test-cache/`) with subdirectories `edges/`, `items/`, and a `history.jls` file. This replaces an earlier design that scattered `test-edges.cache`, `items/`, and `history.jls` across independent paths.

- **Rationale:** A single configurable path controls all test cache artifacts, so a user who relocates the cache (e.g. to a CI ephemeral directory) sets one field, not three. It also mirrors the main `cache.directory` convention (CF-03) and makes cleanup (`titi clean`) straightforward.
- **Alternatives considered:** Separate top-level cache files per artifact — rejected as path proliferation and inconsistent with CF-03.

### Decision 4: Exit codes 10 and 20 for test-selection outcomes

CLI-18 (Phase 1) defines exit codes 0 (success), 1 (failure), 2 (usage). Test selection introduces two outcomes that are neither success nor failure:

- **Exit 10 — confidence-fallback full-suite:** selection confidence fell below `fallbackThreshold`; the system fell back to project-level selection and recommends a full-suite run. Distinct from 1 because the command *succeeded* in producing a recommendation; the recommendation is "run everything".
- **Exit 20 — safe-to-skip:** no tests were selected and the always-run set is empty; it is safe to skip the test phase entirely. Distinct from 0 because the caller (CI) should know *no tests were run* vs. "tests ran and passed".

- **Rationale:** CI orchestration needs to distinguish "ran a filtered subset" (0), "ran everything because we couldn't trust the filter" (10), and "ran nothing because nothing was affected" (20). Collapsing these into 0/1 loses actionable signal.
- **Constraint:** 10 and 20 are emitted only by test-selection commands (`titi test-manifest --select`, `titi affected` when edges are available); non-test commands continue to use 0/1/2 per CLI-18.
- **Alternatives considered:** Emit JSON metadata instead of distinct exit codes — rejected; exit codes are the universal CI contract, JSON requires parsing.

### Decision 5: E011 un-reserved for VSTest failures

DX-02 reserves E010/E011 for "future build/test capabilities". This change is that future capability for E011: VSTest `--list-tests` failure and `titi tests record` test-run failure now raise E011 (aggregatable). E010 remains reserved (no build capability in this change).

- **Rationale:** E011 is aggregatable so `titi tests record` across multiple test projects collects all failures before exiting 1, consistent with DX-05.

## Risks / Trade-offs

| # | Risk | Impact | Mitigation |
|---|------|--------|------------|
| 1 | Coverlet method-level attribution unreliable on .NET 10 (dotnet/coverlet#1654) | Per-test edges may misattribute source files | File-level edges + project-level fallback (Decision 2); validate on real fixture (Task 7.3) |
| 2 | Over-approximation inflates selected set for shared files | CI runs more tests than strictly necessary | Accepted for v1; conservative is safe; future per-line optimisation |
| 3 | VSTest `--list-tests` output format varies across SDK versions / frameworks | Parser may miss tests or misclassify frameworks | Detect JSON vs console format; validate against real xUnit/NUnit/MSTest fixtures (Task 2.8) |
| 4 | `--filter` string length limit (4000 chars) may split awkwardly | Batches may be unbalanced | Batch size configurable (default 100); Traversal SDK handles parallel `<ProjectReference>` items |
| 5 | History file unbounded growth | Disk bloat over time | Retention (100 entries/test) + compaction at 10 MB (TD-06) |
| 6 | Skipped tests could receive spurious edges from coverage runs | Inflated always-run / selected set | Skipped tests receive no edges (TD-03 scenario) |

## Migration Plan

1. Land Phase 1 capabilities (prerequisite).
2. Implement domain types (Section 1).
3. Implement VSTest discovery + parsing (Section 2).
4. Implement coverage + TRX edge construction (Section 3) with the Coverlet fallback (Decision 2).
5. Implement safety invariants + selection (Section 4).
6. Wire CLI commands (Section 5) and configuration (Section 6).
7. Validate against real fixtures (Section 7).
8. Rollback: removing the `test-detection` module leaves all Phase 1 commands functional — the `--select` flag is additive and degrades to project-level when no edges exist.

## Open Questions

1. Should `titi tests record` run incrementally per-project or as a single Traversal? (Current spec: per-project with coverage, aggregated ingestion.)
2. Is the 4000-char `--filter` limit consistent across all supported test frameworks, or does it need per-framework tuning?
3. Should `MissedSelectionIncident` promotion threshold (3 occurrences) be configurable? (Currently fixed at 3, ported from testaruda TIA-SAFE-008.)
