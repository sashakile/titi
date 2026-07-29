---
title: Architecture
---

# Architecture

## Overview

titi is a .NET CLI tool that provides an orchestration layer for .NET monorepos.
It resolves the tension between treating internal modules as independent NuGet packages
(binary mode) vs. local project references (source mode).

## Tech Stack

- **.NET 10 SDK** — target runtime
- **C# 13** — implementation language
- **MSBuild / Microsoft.Build.Graph** — dependency graph analysis
- **Microsoft.VisualStudio.SolutionPersistence** — `.slnx` format
- **xUnit** — unit tests

## Project Structure

```
src/titi/
├── Adapter.cs          # testaruda adapter protocol
├── Affected.cs         # Impact analysis
├── Cli.cs              # CLI argument parsing
├── Config.cs           # Configuration loading
├── Core.cs             # Entry point and command dispatch
├── Coverage.cs         # TRX + Cobertura parsing
├── DiscoveryCache.cs   # Test-item caching
├── Domain.cs           # Core domain model
├── Graph.cs            # Dependency graph
├── HistoryStore.cs     # Run history persistence
├── Ingestor.cs         # Test result ingestion
├── Repl.cs             # Interactive REPL
├── Safety.cs           # Safety invariants
├── SelectionLoader.cs  # Edge/history loading
├── Serialization/      # AOT-safe JSON serialization
├── Solution.cs         # Solution generation
├── Swap.cs             # Reference swapping
├── TestCli.cs          # CLI formatters
├── TestManifest.cs     # Test manifest generation
└── titi.csproj         # Project file
```

## Architecture Patterns

### Functional-core / Imperative-shell

Graph analysis and transformations are pure functions; filesystem I/O and MSBuild
calls are in the shell layer (command handlers).

### Reference Swap

Keep a `PackageReference` with `ExcludeAssets="All"` for NuGet graph resolution;
inject `ProjectReference` via conditional MSBuild when `$(InTitiContext)` is true.

### Naming Convention

NuGet package ID must map deterministically to a filesystem path
(e.g., `Orion.Core.Data` → `src/Orion.Core.Data/`).

### Versioning

- `AssemblyVersion`: always `{Major}.0.0.0` to prevent runtime binding failures
- Cascading bumps: propagate only when public API surface changes
  (checked via ApiCompat); internal-only changes do not cascade

## Key Design Decisions

See `.wai/projects/tracer-bullet/designs/` for detailed design records.

- **Decision 10**: Initial implementation in C# (ClojureCLR.Next deferred until
  MSBuild targets for `.clj` compilation mature)
- **AOT**: Source-generated `JsonSerializerContext` for NativeAOT publish
  compatibility. The adapter subprocess uses reflection-based serialization
  (not AOT-compiled).
