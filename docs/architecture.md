---
title: Architecture
---

# titi Architecture

**TL;DR:** titi is a C# 13 / .NET 10 CLI with functional-core / imperative-shell architecture. It uses MSBuild's project graph for dependency analysis and generates transient `.slnx` solutions with conditional reference swapping.

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
# --- Core & Dispatch ---
├── Core.cs             # Entry point + command dispatch
├── Domain.cs           # Core domain model (records, enums)
├── Config.cs           # titi.config.edn loading + defaults
├── Interop.cs          # MSBuild interop (locator + graph eval)

# --- Dependency Graph ---
├── Graph.cs            # MonorepoGraph construction
├── Repl.cs             # Interactive dependency graph REPL

# --- Reference Management ---
├── Swap.cs             # Reference swapping (PackageRef ↔ ProjectRef)
├── Solution.cs         # .slnx solution generation
├── Affected.cs         # Impact analysis (git diff → affected projects)

# --- Test Discovery & Coverage ---
├── TestDiscovery.cs    # `dotnet test --list-tests` parsing
├── Coverage.cs         # TRX + Cobertura XML parsing
├── EdgeBuilder.cs      # Build test→source edges from runs
├── DiscoveryCache.cs   # Test-item caching (fingerprint-keyed)

# --- Safety, Selection & Ingestion ---
├── Safety.cs           # Selection safety (always-run, confidence)
├── SelectionLoader.cs  # Edge/history loading from cache
├── Ingestor.cs         # Test result ingestion + correlation
├── HistoryStore.cs     # Run history persistence (EDN format)
├── ArtifactLocator.cs  # Find TRX/Cobertura in test results dir
├── RecordPlanner.cs    # Test-run planning + incremental fingerprints

# --- Output & Protocol ---
├── Adapter.cs          # testaruda adapter protocol
├── TestCli.cs          # CLI output formatters
├── TestManifest.cs     # Traversal .proj + filter generation
├── Serialization/      # AOT-safe JSON (TitiJsonContext + DTOs)
│   └── TitiJsonContext.cs
└── titi.csproj         # Project file
```

## Architecture Patterns

### Functional-core / Imperative-shell

Graph analysis and transformations are pure functions; filesystem I/O and MSBuild
calls are in the shell layer (command handlers in `Core.cs`).

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
- **AOT**: Source-generated `JsonSerializerContext` (`Serialization/`) for
  NativeAOT publish compatibility. The adapter subprocess uses reflection-based
  serialization (not AOT-compiled) — see `JsonSerializerIsReflectionEnabledByDefault`
  in `titi.csproj`.
