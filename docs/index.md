---
title: titi — .NET Monorepo Orchestration CLI
---

# titi

[![tracked with wai](https://img.shields.io/badge/tracked%20with-wai-blue)](https://github.com/charly-vibes/wai)

**titi is a CLI tool that orchestrates .NET monorepos by swapping
`PackageReference` ↔ `ProjectReference`, generating transient solutions,
and computing test-impact analysis from git diff + coverage data.**

Small Mono Repo tool for C# Projects.

## Status

The tracer bullet (`titi open <package-id>`) and the testaruda adapter
(Phase 1 + Phase 2 method-level granularity) are implemented and working.
219+ tests pass. Core test-impact-analysis primitives (test-item detection,
edges, selection, safety model) are built.

**Remaining work:** cascading version bumps (ApiCompat-based), production
hardening.

## Core Capabilities

| Capability | Command | Status | Description |
|------------|---------|--------|-------------|
| **Reference swapping** | `titi open` | ✅ Implemented | Toggle `PackageReference` ↔ `ProjectReference` via MSBuild conditional logic |
| **Dynamic solution generation** | `.slnx` output | ✅ Implemented | Transient `.slnx` files for a specific project's dependency closure |
| **Test Impact Analysis** | `titi affected` | ✅ Implemented | Compute affected set from git diff + dependency graph |
| **Test-item detection** | `titi tests list` | ✅ Implemented | Enumerate test methods via VSTest `--list-tests` |
| **Coverage-to-edge mapping** | `titi tests ingest` | ✅ Implemented | Cobertura XML → test-to-source dependency edges |
| **Test selection** | `titi affected` output | ✅ Implemented | Always-run set, confidence scoring, fallback logic |
| **testaruda adapter** | `titi testaruda-adapter` | ✅ Implemented | JSON-over-stdio adapter for testaruda integration |
| **Interactive REPL** | `titi repl` | ✅ Implemented | Explore the dependency graph interactively |
| **Cascading version bumps** | — | 🔄 Planned | ApiCompat-based version increment detection |

## Getting Started

```bash
just build      # Build the CLI
just test       # Run 219+ tests (excludes slow synthetic-fixture builds)
just smoke      # Run titi clean → open → affected against the sample-monorepo fixture
```

> `just smoke` runs end-to-end against `test/fixtures/sample-monorepo/`. It
> generates a `.slnx`, reports affected projects, and cleans up — no prior
> setup required.

## Documentation

- [CLI Reference](cli.md) — all commands, flags, and exit codes
- [Architecture](architecture.md) — tech stack, project structure, design decisions
- [Safety Model](safety.md) — test selection safety invariants and confidence scoring
- [testaruda Adapter](adapter.md) — JSON-over-stdio adapter protocol
- [Openspec Specs](specs/bundles/spec.md) — formal capability specifications
