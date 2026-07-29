---
date: "2026-03-10"
project: "tracer-bullet"
sessions_analyzed: 0
type: reflection
---

## Project-Specific AI Context
_Last reflected: 2026-03-10 · 5 sessions analyzed_

### Conventions
- **Spec-first workflow**: All capabilities defined in `openspec/specs/` before implementation begins. 10 specs cover the full CLI surface.
- **ClojureCLR on .NET 10**: Implementation uses ClojureCLR.Next targeting .NET 10; `Microsoft.Build.Locator.RegisterDefaults()` must be called before any MSBuild type access.
- **Functional-core / imperative-shell**: Graph analysis and transformations are pure functions; filesystem I/O and MSBuild calls live in the shell layer.
- **TDD required**: Each ticket maps to a red→green→refactor cycle; refactoring in separate commits.
- **EDN config format**: `titi.config.json` with defaults applied when absent (versionPolicy defaults to SEMVER_COMPATIBLE).

### Common Gotchas
- **NuGet 6.12 CPM regression** (NuGet/Home#13938): Must set `RestoreUseLegacyDependencyResolver=true` when `CentralPackageTransitivePinningEnabled=true`.
- **AssemblyVersion must be `{Major}.0.0.0`**: Full version in AssemblyVersion causes runtime binding failures across patches.
- **Cascading bumps only propagate on API surface changes**: Internal-only patches do NOT cascade to dependents. ApiCompat (`Microsoft.DotNet.ApiCompat.Tool`) gates propagation.
- **Reference swap retains PackageReference**: When swapping to source, the original `PackageReference` stays with `ExcludeAssets="All"` for NuGet graph resolution; `ProjectReference` is injected alongside.
- **Transitive floor validation**: Before swapping, check that local source version satisfies transitive floors from binary dependencies in other graph paths.

### Architecture Notes
- **10 openspec specs**: domain-model, dependency-graph, reference-swap, configuration, cli, solution-generation, graph-cache, msbuild-integration, versioning, diagnostics.
- **Phase 1 tracer bullet**: `titi open`, `titi affected`, `titi clean` — exercises full stack from config→graph→swap→solution-gen.
- **Implementation plan**: Phase 1a (skeleton + fixtures) → 1b (core namespaces) → 1c (commands) → 1d (tests) → 1e (build/distribution).
- **Cache strategy**: Tiered invalidation — subgraph rebuild on .csproj change, full rebuild on global trigger / version change / maxAge exceeded.
- **Test tiers**: unit, package, integration, compatibility — configured via glob patterns in `titi.config.json`.
