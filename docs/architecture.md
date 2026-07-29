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
├── Affected.cs         # Impact analysis (git diff → affected projects)
├── ArtifactLocator.cs  # Find TRX/Cobertura in test results dir
├── Config.cs           # titi.config.edn loading + defaults
├── Core.cs             # Entry point + command dispatch
├── Coverage.cs         # TRX + Cobertura XML parsing
├── DiscoveryCache.cs   # Test-item caching (fingerprint-keyed)
├── Domain.cs           # Core domain model (records, enums)
├── EdgeBuilder.cs      # Build test→source edges from runs
├── Graph.cs            # MonorepoGraph construction
├── HistoryStore.cs     # Run history persistence (EDN format)
├── Ingestor.cs         # Test result ingestion + correlation
├── Interop.cs          # MSBuild interop (locator + graph eval)
├── RecordPlanner.cs    # Test-run planning + incremental fingerprints
├── Repl.cs             # Interactive dependency graph REPL
├── Safety.cs           # Selection safety (always-run, confidence)
├── SelectionLoader.cs  # Edge/history loading from cache
├── Serialization/      # AOT-safe JSON (TitiJsonContext + DTOs)
│   └── TitiJsonContext.cs
├── Solution.cs         # .slnx solution generation
├── Swap.cs             # Reference swapping (PackageRef ↔ ProjectRef)
├── TestCli.cs          # CLI output formatters
├── TestDiscovery.cs    # `dotnet test --list-tests` parsing
├── TestManifest.cs     # Traversal .proj + filter generation
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
