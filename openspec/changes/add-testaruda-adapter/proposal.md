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
- **Not resolved by this proposal**: Decision 4 (component-graph scope for `static-deps`) and Decision 2 (adapter process-lifetime model) require testaruda-side source review or a maintainer decision before implementation can start — see `design.md` for details. TRX-parsing ownership is a titi-internal packaging decision.

## Dependencies

- **testaruda adapter protocol**: targets testaruda commit `e8fad5b` (tagged `v0.2.0`). TIA-ADAPT-001–016 at this commit are the normative reference for the JSON-over-stdio protocol.
- **titi capabilities**: depends on existing `dependency-graph` (DG-01–09, `MonorepoGraph`/`AffectedSet`/`FileFingerprint`) and `cli` (CLI-06, `titi test-manifest` Traversal .proj generator). No changes to these specs are required.

## Next Steps

1. Review and approve this proposal.
2. Resolve the two blocking decisions: Decision 2 (process-lifetime model) and Decision 4 (component-graph scope) — see `design.md`.
3. Create beads issues for Phase 1 implementation tasks.
4. Begin implementation per `tasks.md`, starting with pre-implementation items (sections 1.1–1.4).
