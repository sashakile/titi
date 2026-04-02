# Project Context

## Purpose

titi is a ClojureCLR-based CLI tool that provides an orchestration layer for
.NET monorepos. It resolves the tension between treating internal modules as
independent NuGet packages (binary mode) vs. local project references (source
mode). The tool automates reference swapping, solution generation, dependency
graph analysis, cascading version bumps, and test impact analysis.

## Tech Stack

- **ClojureCLR / ClojureCLR.Next** — implementation language
- **.NET 10 SDK** — target runtime; pinned via `global.json`
- **MSBuild / Microsoft.Build.Graph** — dependency graph analysis
- **Microsoft.VisualStudio.SolutionPersistence** — `.slnx` solution management
- **Microsoft.Build.Traversal** — parallel CI build orchestration
- **Nerdbank.GitVersioning (NBGV)** — per-project versioning with `version.json`
- **NuGet Central Package Management** — `Directory.Packages.props` + lock files
- **Microsoft.DotNet.ApiCompat** — API surface change detection

## Project Conventions

### Code Style

- Clojure: 2-space indentation (enforced by `.editorconfig`); `cljfmt` for formatting
- C#/MSBuild XML: 4-space / 2-space indentation respectively (see `.editorconfig`)
- `LF` line endings throughout
- Functional-core / imperative-shell: graph analysis and transformations are
  pure functions; filesystem I/O and MSBuild calls are in the shell layer

### Architecture Patterns

- **Reference swap**: keep a `PackageReference` with `ExcludeAssets="All"` for
  NuGet graph resolution; inject `ProjectReference` via conditional MSBuild when
  `$(InTitiContext)` is true
- **Naming convention**: NuGet package ID must map deterministically to a
  filesystem path (e.g., `Company.Core.Data` → `src/Company.Core.Data/`)
- **AssemblyVersion**: always `{Major}.0.0.0` to prevent runtime binding failures
- **CPM + transitive pinning**: `CentralPackageTransitivePinningEnabled=true`;
  `RestoreLockedMode=true` in CI only
- **Cascading bumps**: propagate only when public API surface changes
  (checked via ApiCompat); internal-only changes do not cascade

### Testing Strategy

- Unit tests live alongside source under `test/titi/`
- `dotnet test` with `XPlat Code Coverage` (Cobertura format)
- Test Impact Analysis: only run tests for projects affected by the current diff
- CI enforces `RestoreLockedMode=true` via the `CI` environment variable

### Git Workflow

- Single `main` branch; feature branches for non-trivial changes
- Conventional commits encouraged (used by `titi version detect` for bump type)
- Each PR touching library code must include a changeset file
- `prek` runs `trailing-whitespace`, `end-of-file-fixer`, `check-yaml`,
  `check-xml`, `check-added-large-files` on every commit
- Commit workflow: use the `commit` skill (via wai)

## Domain Context

The core problem is the "Coherency Problem" in .NET monorepos: as the project
graph grows, manually swapping `PackageReference` ↔ `ProjectReference` is
error-prone and breaks builds for teammates who don't have the full source tree.
titi automates this via MSBuild conditional logic and a project-graph-aware CLI.

Key MSBuild concepts:
- `ProjectGraph` (static evaluation, no compilation) for graph analysis
- `Directory.Build.props` / `Directory.Build.targets` for repo-wide settings
- `Directory.Packages.props` for Central Package Management
- `.slnx` (XML-based solution format, .NET 9+) for dynamic solution generation

## Important Constraints

- `Microsoft.Build.Locator.RegisterDefaults()` must be called before any
  MSBuild types are loaded in the ClojureCLR process
- AOT compilation requires `(:gen-class :main true)` in the entry-point namespace
- NuGet 6.12+ new resolver has a known regression with CPM transitive pinning
  (NuGet/Home#13938); workaround: `RestoreUseLegacyDependencyResolver=true`
- Lock files are per-project, not per-repo; CPM handles version consistency,
  lock files handle reproducibility

## External Dependencies

- **NuGet.org** — external package feed
- **GitHub Actions** — CI/CD (`.github/workflows/ci.yml`)
- **Codecov** — coverage reporting (optional, fails silently if unavailable)
- **dolt** — local database backend for `bd` issue tracker (port 13627)
