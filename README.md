# titi

[![tracked with wai](https://img.shields.io/badge/tracked%20with-wai-blue)](https://github.com/charly-vibes/wai)
[![NuGet](https://img.shields.io/nuget/v/titi)](https://www.nuget.org/packages/titi)

> **⚠ AI-Assisted Development.** This project is built through a structured Human–AI
> pair-programming workflow. Substantive changes are tracked in specifications and
> tickets, tested, reviewed with automated tools, and approved by the maintainer.
> The project has **not** had a professional security audit or human line-by-line
> review of the entire codebase.

Small Mono Repo tool for C# Projects.

## Status: Tracer Bullet Complete

The tracer bullet (`titi open <package-id>`) and testaruda adapter (Phase 1 + Phase 2) are implemented. 433+ tests pass.
Core test-impact-analysis primitives (test-item detection, edges, selection, safety model) are built.
Next: cascading version bumps and production hardening.

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
| **testaruda adapter** | ✅ `titi testaruda-adapter` | JSON-over-stdio adapter for testaruda integration |

## CLI Reference

```
titi open <package-id>      Generate transient .slnx with reference swapping
titi affected [--base]      List projects affected by current changes
titi tests list <csproj>    Enumerate test items in a project
titi tests ingest <trx>     Ingest test results and coverage
titi tests record           Run all tests and collect results
titi testaruda-adapter      Start testaruda adapter (JSON-over-stdio protocol)
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

See [Safety Model](docs/safety.md) for the full reference — always-run set, confidence scoring formula, fallback thresholds, and missed-selection incident handling.

## Relation to testaruda

See [testaruda Adapter](docs/adapter.md) for the adapter protocol, granularity model, and known limitations.

## testaruda Adapter

titi ships a built-in testaruda adapter subcommand (`titi testaruda-adapter`)
that speaks the [testaruda](https://github.com/charly-vibes/testaruda) JSON-over-stdio
adapter protocol. See [full adapter documentation](docs/adapter.md) for the
protocol commands, granularity model, and known limitations.

### Cold-Start Benchmark

CLR process cold-start (`--help` only, 5 samples):

| Mode   | Mean  | StdDev | Min  | Max  | mean+2σ |
|--------|-------|--------|------|------|---------|
| JIT    | 711ms | 19ms   | 691ms| 740ms| 749ms   |
| AOT    | N/A   | N/A    | N/A  | N/A  | N/A     |

> AOT is not currently viable: `System.Text.Json` uses reflection-heavy
> serialization throughout the codebase (adapter, CLI, cache), causing linker
> errors and IL3050 warnings when `PublishAot=true`. Tracked as future work.

All measured cold-starts are well under the 30s testaruda default timeout.
Budget the adapter timeout for **graph-build time**, not CLR cold-start.
Set `testaruda.toml` minimum adapter timeout to 30s (small repos) or 60s (large repos).
Re-run with `just benchmark-adapter-coldstart` to reproduce.

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
just test       # Run 219+ tests (excludes slow synthetic-fixture builds)
just smoke      # Run end-to-end smoke test
just titi-open  # Run titi open against sample-monorepo fixture
```
