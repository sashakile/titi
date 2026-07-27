# Change: Add a testaruda adapter subcommand to titi

## Why

testaruda (github.com/charly-vibes/testaruda) is a language-agnostic CLI that computes the minimal test set affected by a code change, via a JSON adapter protocol. It has no C#/.NET adapter. titi already solves the hard, .NET-specific half of that problem: it builds a full `MonorepoGraph` from `.csproj` files, computes an `AffectedSet` from git diffs, and emits test-scoped MSBuild Traversal projects.

Building a `testaruda-adapter-dotnet` from scratch would mean re-implementing MSBuild project-graph evaluation, `.csproj` parsing, reference resolution, and graph caching — machinery titi already has. This change adds a thin additive seam so testaruda can consume titi's already-correct project graph as its C#/.NET static-dependency source, while titi remains fully standalone.

This is a different shape from the earlier Testimonial.jl integration (where a single-purpose tool was folded wholesale into testaruda): titi is a multi-purpose .NET monorepo orchestrator where test-impact analysis is one capability among several.

## What Changes

- **New titi subcommand**: `titi testaruda-adapter` — a long-lived subprocess speaking the JSON-over-stdio adapter protocol (Phase 1: project-level granularity, `symbol_model_complete: false`).
- **testaruda-side config defaults** (not in this repo): `.csproj`/`.sln`/`.slnx` → `titi testaruda-adapter` mapping, dotnet-project detection.
- **Phase 1 scope**: project-level granularity — each test item is a whole test assembly. Benefits are composability, caching, confidence scoring, and safety-net, not raw test-count reduction.
- **Phase 2 (deferred)**: test-method-level granularity via VSTest `--list-tests` + TRX ingestion.

## Impact

- Affected specs (titi): `cli` — new `titi testaruda-adapter` command.
- Affected specs (testaruda, not in this repo): `adapter-protocol` — new dotnet detection defaults.
- Affected code (titi): new subcommand module in `src/titi/`, reusing `MonorepoGraph`, `AffectedSet`, and `FileFingerprint` without modification.
- **Not affected**: All existing titi commands (`titi affected`, `titi test-manifest`, `titi build-manifest`, `titi open`, `titi pkg`, `titi check`, `titi audit`, `titi version`, `titi bundle`, `titi repl`) remain fully standalone and unchanged.
- **Resolved by source review** (see `design.md`): Decision 2 (adapter process-lifetime model) and Decision 4 (component-graph scope for `static-deps`) were both resolved against testaruda's actual `AdapterIO` implementation and store architecture. TRX-parsing ownership is a titi-internal packaging decision (see SEQ-1 / `titi-2bz`, which defers to whichever change lands first between this proposal and `add-test-item-detection`).

## Dependencies

- **testaruda adapter protocol**: targets testaruda `v0.2.3` (commit `34e8db6`, 2026-07-18). The titi adapter protocol contract is inlined in CLI-19 (see `specs/cli/spec.md`) so this proposal is self-contained; the testaruda specs (`openspec/specs/adapter-protocol/spec.md` TIA-ADAPT-001–013, `openspec/specs/composability/spec.md` TIA-COMP-001–012) remain the authoritative reference. The earlier pin to `e8fad5b`/`v0.2.0` is superseded.
- **titi capabilities**: depends on existing `dependency-graph` (DG-01–09, `MonorepoGraph`/`AffectedSet`/`FileFingerprint`) and `cli` (CLI-06, `titi test-manifest` Traversal .proj generator). No changes to these specs are required.
- **External (testaruda repo)**: `.csproj`/`.sln`/`.slnx` → `titi testaruda-adapter` mapping and dotnet-project detection defaults must be added to the testaruda repository. No testaruda-side change for .NET detection exists as of v0.2.3 (tracked as `bd` issue `titi-co9`).

## Next Steps

1. Review and approve this proposal.
2. Resolve the one remaining external blocker: testaruda-side config defaults for .NET detection (`titi-co9`) — must be proposed in the testaruda repo.
3. Resolve SEQ-1 (`titi-2bz`): decide TRX-parser ownership ordering between this change and `add-test-item-detection`.
4. Create beads issues for Phase 1 implementation tasks (the two source-review blockers are already resolved).
5. Begin implementation per `tasks.md`, starting with pre-implementation items (sections 1.1–1.4).
