## Context

Two systems already solve overlapping halves of "what tests does this C# change affect": testaruda's language-agnostic core (selection engine, provenance semiring, confidence scoring, safety net) and titi's .NET-specific project graph (MSBuild-accurate reference resolution, affected-set computation, tiered test partitioning). Nobody proposed either system replace the other. This document works out how a thin seam between them should be shaped.

## Goals / Non-Goals

**Goals:**
- testaruda gains a working C#/.NET adapter without re-implementing MSBuild evaluation.
- titi remains fully usable standalone; nothing about its existing commands, config, or audience changes.
- The seam is additive: one new titi subcommand, no new titi dependency on testaruda's crates, no new repo.

**Non-Goals:**
- Method-level test selection in Phase 1 (deferred — see Phase 2 tracking).
- Replacing `titi affected` / `titi test-manifest` — they stay, unchanged.
- Resolving F#/VB.NET support — MSBuild project-graph evaluation is language-neutral, likely free, but untested.

## Decisions

### Decision 1: Adapter lives inside titi as a subcommand, not a new binary

The adapter SHALL be a new `titi testaruda-adapter` subcommand, not a separate binary or repository.

- **Rationale**: titi's ClojureCLR process already has `Microsoft.Build.Locator` registered and a working, cached `MonorepoGraph`. A separate binary would re-register MSBuild, re-scan `.csproj` files, and maintain a second graph cache independent of `.titi/graph.cache` — two lock files, two staleness windows, no shared benefit.
- **Alternatives considered**: (1) Separate `testaruda-adapter-dotnet` binary — rejected as pure duplication with no compensating benefit, unlike the Julia case where no prior art existed. (2) testaruda core embedding MSBuild logic directly — violates TIA-ARCH-008.
- **Constraint**: `Microsoft.Build.Locator.RegisterDefaults()` is process-global and can only be called once per process. Keeping the adapter as a dedicated subprocess invocation avoids double-registration.

### Decision 2: One long-lived adapter process per testaruda invocation, not one spawn per command

The adapter SHALL be a single long-lived process for the duration of one testaruda invocation: it builds (or loads from `.titi/graph.cache`) the `MonorepoGraph` once on `discover` (or handshake), holds it in memory, and answers `static-deps`/`fingerprint`/`run-args` against the same in-memory graph.

- **Rationale**: titi's own performance model (DG-08) budgets 30 seconds for a cold graph build on a 1000-project repo. If testaruda spawned a fresh process per command, that budget would be paid multiple times per invocation. This is the only way titi's graph-caching investment (DG-09, GC-07) transfers to the adapter role.
- **Risk**: This needs confirming against testaruda's actual `AdapterIO` implementation (not just the spec) — see risk row 1.
- **Blocked**: Must be resolved before Phase 1 implementation starts.

### Decision 3: Command mapping — reuse titi's existing types verbatim

| testaruda command | titi source |
|---|---|
| handshake | New: name=`titi`, languages=`["csharp"]`, granularity=`project` (Phase 1), `{symbol_model_complete: false}` (F#/VB.NET deferred — see risk 7) |
| `discover` | One test item per project where `ProjectDescriptor.isTestProject = true` (DM-01) |
| `static-deps` | `AffectedSet` computation (DG-04) reused directly; K-value = multiplicative identity per TIA-ADAPT-009 |
| `fingerprint` | `FileFingerprint` (DG-07) reused with no changes |
| `run-args` | Reuse `titi test-manifest` Traversal-.proj generator (CLI-06) as the collection path. The adapter writes the Traversal .proj to a temp path (or `.titi/`), returns the `dotnet test` invocation, and cleans up after test execution completes (or on adapter shutdown). |
| `ingest` | **New work**: TRX output parsing — titi has no per-test result parser today |

Only `ingest` is genuinely new code. Everything else is a thin marshalling layer.

### Decision 4: Whole-graph adapter (option b) for component-graph scope

In testaruda's terminology, a "component" is a deployable unit in a polyglot monorepo — the granularity at which testaruda's `composability` capability (TIA-COMP-001–012) computes bottom-up affected sets. titi's `MonorepoGraph` maps each .NET project to one such component node.

The adapter's `static-deps` response SHALL accept the full changed-file set for the repo and return the entire cross-component affected set in one call, using titi's already-topologically-sorted graph directly.

- **Rationale (against option a, "dumb adapter")**: testaruda's own bottom-up component resolution (TIA-COMP-003) would need cross-component dependency edges that only titi's `MonorepoGraph` has, and the adapter protocol has no command to hand over a whole component graph. Under option (a), this doesn't actually work without inventing a new protocol command.
- **Rationale (for option b)**: It matches what titi already computes today (DG-04's `transitivelyAffected` is bottom-up component resolution), avoids the protocol gap in (a), and sidesteps parallel-invocation contention on `.titi/graph.cache.lock` under TIA-COMP-008.
- **Status**: Blocking — cannot be settled from specs alone. Needs either testaruda source review of `AdapterIO`/composability-engine or a maintainer decision.
- **Spec implication**: If adopted, this means `composability`'s TIA-COMP-003 is intentionally not exercised for adapter-owned components — a spec clarification testaruda's maintainers need to make.

## Risks / Trade-offs

| # | Risk | Impact | Mitigation |
|---|------|--------|------------|
| 1 | Adapter process-lifetime model (Decision 2) is not confirmed against testaruda's actual `AdapterIO` implementation — per-command spawning would make the adapter unusably slow on large monorepos | If per-command spawning is the reality, the 30s cold-graph-build budget (DG-08) is paid multiple times per invocation | Spike against testaruda's actual `AdapterIO` implementation before writing any titi code |
| 2 | `static-deps` response scope ambiguity (Decision 4) is genuinely unresolved in the adapter protocol spec | Choosing wrong either duplicates titi's bottom-up walk in testaruda's core or requires a protocol extension that doesn't exist | Resolve explicitly before implementation via maintainer decision or source review |
| 3 | ClojureCLR/.NET CLR process startup latency is a distinct cost from the graph-build budget — paid on every subprocess spawn | An adapter timeout (TIA-ADAPT-013) could be tripped by cold-start overhead alone | Benchmark titi's process cold-start time (AOT vs. non-AOT) and document minimum timeout |
| 4 | Two independent lock/cache mechanisms (DG-09's `.titi/graph.cache.lock` and testaruda's per-component cache) could interact | Stale `.titi/graph.cache` could feed a wrong fingerprint into testaruda's cache | No new mitigation beyond DG-09; document as a known limitation |
| 5 | Phase 1 project-level granularity means large test projects are selected wholesale | No regression vs. `titi test-manifest`, but testaruda's method-level value doesn't materialize for .NET until Phase 2 | Scope Phase 1 value to composability/caching/safety-net, not raw test-count reduction |
| 6 | `ingest` (TRX parsing) is new, untested code with no existing coverage | Malformed TRX could silently misattribute pass/fail results | TIA-ADAPT-012 fallback (all-tests) is the right safety net; rely on it |
| 7 | F#/VB.NET support is asserted as "likely free" but not verified against fixtures | Silent gap if tier-glob heuristic assumes `.cs` conventions | Verify against a real mixed C#/F# fixture before claiming multi-language support |
| 8 | TIA-COMP-008 (parallel per-component selection) was never reconciled against DG-09's single-writer lock | Parallel adapter invocations could contend for `.titi/graph.cache.lock` | Run `titi cache warm` once before invoking testaruda; design adapter for read-only cache access at query time |

## Open Questions

1. **Does testaruda's core have precedent for an adapter answering `static-deps` for a whole monorepo in one call?** The shipped Rust/Python adapters are the only prior art; neither operates in a multi-project monorepo context. Needs a maintainer with access to actual adapter source.
2. **Should handshake advertise `component_graph: true` as a capability flag?** If Decision 4 option (b) is adopted, this is a new capability the protocol has no vocabulary for yet.
3. **Who owns TRX-parsing long-term?** If titi's maintainers don't want TRX as a first-class capability, it may belong in a separate module.
4. **Is Phase 2 (method-level granularity) worth building?** Unlike the Julia case, Phase 2 would be genuinely new capability neither system has. A prioritization call.
5. **Does `dotnet test --list-tests` reliably enumerate test methods across xUnit/NUnit/MSTest?** Not verified — needed before Phase 2 scoping.

## Risk Mitigation Mapping

Each risk identified above is addressed by specific tasks in `tasks.md`:

| Risk | Mitigation Task(s) |
|------|--------------------|
| 1 (process-lifetime model) | 1.2 (confirm against `AdapterIO`), 1.3 (benchmark cold-start) |
| 2 (static-deps scope) | 1.1 (resolve Decision 4) |
| 3 (CLR startup latency) | 1.3 (benchmark AOT vs non-AOT), 6.2 (document minimum timeout) |
| 4 (lock/cache interaction) | 6.1 (document limitation) |
| 5 (project-level granularity) | 5.2 (verify fixture agreement) |
| 6 (TRX parsing) | 3.1–3.2 (implement + wire), rely on TIA-ADAPT-012 fallback |
| 7 (F#/VB.NET support) | 6.1 (document status), future fixture verification |
| 8 (parallel selection contention) | 1.2 (confirm model), 2.6 (read-only cache design) |

## Migration Plan

1. Resolve Decision 4 (component-graph scope) — blocking.
2. Confirm Decision 2 (process-lifetime model) against testaruda's `AdapterIO` — blocking.
3. Implement `titi testaruda-adapter` handshake + `discover` + `static-deps` + `fingerprint` + `run-args` (Phase 1).
4. Implement `ingest` (TRX parsing).
5. Add testaruda-side config defaults (tracked in testaruda repo, not here).
6. Fixture: synthetic .NET monorepo exercised through both `titi test-manifest` and testaruda's engine via the adapter.
7. Phase 2 (separate change, not scheduled).
8. Rollback: the adapter subcommand can be removed with zero impact on other titi commands.
