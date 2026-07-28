# titi

[![tracked with wai](https://img.shields.io/badge/tracked%20with-wai-blue)](https://github.com/charly-vibes/wai)

Small Mono Repo tool for C# Projects.

## Status: Tracer Bullet Complete

The tracer bullet (`titi open <package-id>`) is implemented and working. 58 tests pass.
Core test-impact-analysis primitives (test-item detection, edges, selection) are built.
Next: testaruda adapter integration and production hardening.

## Purpose

titi is a CLI tool for .NET monorepo orchestration. It resolves the tension between
treating internal modules as independent NuGet packages (binary mode) vs. local project
references (source mode).

## Core Capabilities

| Capability | Status | Description |
|------------|--------|-------------|
| **Reference swapping** | ✅ `titi open` | Toggle `PackageReference` ↔ `ProjectReference` via MSBuild conditional logic |
| **Dynamic solution generation** | ✅ `.slnx` output | Transient `.slnx` files for a specific project's dependency closure |
| **Test Impact Analysis (project-level)** | ✅ `titi affected` | Compute affected set from git diff + dependency graph |
| **Test-item detection** | ✅ `titi tests list` | Enumerate test methods via VSTest `--list-tests` |
| **Coverage-to-edge mapping** | ✅ `titi tests ingest` | Cobertura XML → test-to-source dependency edges |
| **Test selection** | ✅ `titi affected` output | Always-run set, confidence scoring, fallback logic |
| **Cascading version bumps** | 🔄 Planned | ApiCompat-based version increment detection |

## CLI Reference

```
titi open <package-id>      Generate transient .slnx with reference swapping
titi affected [--base]      List projects affected by current changes
titi tests list <csproj>    Enumerate test items in a project
titi tests ingest <trx>     Ingest test results and coverage
titi tests record           Run all tests and collect results
titi clean                  Remove all titi-generated artifacts
titi --help                 Show usage
```

### titi open

Generates a `.slnx` solution with one project's transitive dependency closure
swapped from binary (NuGet) to source (ProjectReference). The swap retains
the original `PackageReference` with `ExcludeAssets=All` and injects a
`ProjectReference` alongside, controlled by `InTitiContext=true`.

### titi affected

Runs `git diff` between refs, maps changed files to projects via the
dependency graph, and computes transitive impact. With test edges available,
includes per-test selection results and confidence score.

### titi tests

- `tests list <csproj>` — invokes `dotnet test --list-tests` and parses JSON
  output into `TestItem` records. Detects xUnit/NUnit/MSTest from executor URI.
- `tests ingest <trx> [--coverage <cobertura>]` — parses TRX test results and
  Cobertura coverage XML, building `TestToSourceEdge` records and caching them.
- `tests record` — runs all test projects with coverage collection, then ingests.

## Safety Model

Test selection uses a multi-factor safety model:

1. **Always-run set**: tests that failed last run, are newly added, or have no
   history are always selected regardless of coverage edges.
2. **Confidence scoring**: weighted combination of changed-file resolution ratio
   (60%), edge freshness (25%), and history depth (15%).
3. **Fallback**: when confidence drops below the threshold (default 0.7),
   selection falls back to project-level (all tests in affected projects).
4. **Missed-selection incidents**: when a test is missed by selection but should
   have been selected, the incident is recorded and can promote the edge weight.

## Relation to testaruda

titi's test-item detection primitives (TID-1 through TID-8) are independent of
the testaruda adapter. The adapter's **Phase 1** runs at project granularity
using titi's existing `MonorepoGraph` and `AffectedSet`. **Phase 2** (deferred)
will consume these test-item primitives for method-level granularity.

This separation means:
- `add-test-item-detection` is a prerequisite for adapter Phase 2, not a replacement
- The adapter can ship at project granularity while test-item features mature
- Both changes are compatible and additive

## Implementation Language

Initial implementation is in C# following the architecture's namespace structure
(`titi.interop`, `titi.config`, `titi.graph`, `titi.swap`, `titi.solution`,
`titi.core`). The ClojureCLR.Next NuGet package (1.12.2) lacks MSBuild targets
for compiling `.clj` files; migration is deferred until the toolchain matures.
See Decision 10 in `.wai/projects/tracer-bullet/designs/`.

## Tech Stack

- **.NET 10 SDK** (Runtime)
- **C# 13** (Implementation — ClojureCLR.Next deferred)
- **MSBuild / Microsoft.Build.Graph** (Dependency Graph Analysis)
- **Microsoft.VisualStudio.SolutionPersistence** (.slnx format)
- **xUnit** (unit tests)

## Project Structure

- `src/titi/` — CLI source code (Domain, Interop, Config, Graph, Swap, Solution,
  Core, Affected, TestDiscovery, Coverage, Safety, TestCli)
- `test/titi/` — Tests (unit + integration)
- `test/fixtures/` — Test fixtures (sample-monorepo, synthetic-monorepo)
- `openspec/` — Specifications for capabilities
- `.wai/` — Research and architecture decisions

## Getting Started

```bash
just build      # Build the CLI
just test       # Run 58 tests
just smoke      # Run end-to-end smoke test
just titi-open  # Run titi open against sample-monorepo fixture
```
