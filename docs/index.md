---
title: titi — .NET Monorepo Orchestration CLI
---

# titi

[![tracked with wai](https://img.shields.io/badge/tracked%20with-wai-blue)](https://github.com/charly-vibes/wai)

Small Mono Repo tool for C# Projects.

## Status: Tracer Bullet Complete

The tracer bullet (`titi open <package-id>`) is implemented and working. 219+ tests pass.
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

## Getting Started

```bash
just build      # Build the CLI
just test       # Run tests
just smoke      # Run end-to-end smoke test
```

## CLI Reference

See the [CLI Reference](cli.md) page for all commands.

## Architecture

See the [Architecture](architecture.md) page for design decisions.

## Safety Model

See the [Safety Model](safety.md) page for test selection safety invariants.

## testaruda Adapter

See the [Adapter Protocol](adapter.md) page for the JSON-over-stdio adapter.
