Tracer-bullet implementation plan — titi open <packageId>

MILESTONE: end-to-end working 'titi open Orion.Core.Data' that:
  1. Loads titi.config.edn (or applies defaults if absent)
  2. Discovers all *.csproj files under sourceRoot
  3. Builds ProjectGraph from discovered entry-points
  4. Computes swap (Orion.Core.Data → ProjectReference)
  5. Generates .titi/solutions/Orion.Core.Data.slnx
  6. Reports SwapResult to stdout

## Phase 1a — Project skeleton + test fixture (implement first)

CLI project:
- [ ] Create src/titi/ directory
- [ ] Add src/titi/titi.csproj — .NET console project (Microsoft.NET.Sdk) referencing
      ClojureCLR.Next NuGet packages; NOT a special ClojureCLR project type
- [ ] Add .titi/ to .gitignore

Test fixture (needed before integration tests can be written):
- [ ] Create test/fixtures/sample-monorepo/ with:
      - Orion.Core.Data/Orion.Core.Data.csproj (library, PackageId=Orion.Core.Data)
      - Orion.App/Orion.App.csproj (console app with PackageReference to Orion.Core.Data 1.0.0)
      - titi.config.edn with {:prefix "Orion." :source-root "./"}
      - Directory.Build.props stub (TitiPrefix, TitiSourceRoot, InTitiContext defaults)
      - Directory.Build.targets with swap logic (ExcludeAssets=All + ProjectRef injection)
- [ ] Note: Directory.Build.props/.targets belong in the TEST FIXTURE and in
      consumer repos — NOT in the titi CLI project itself

## Phase 1b — Core namespaces (implement in listed order — ordering is load-order critical)

- [ ] titi.interop — MUST be first; calls MSBuildLocator/RegisterDefaults before
      any MSBuild type is referenced; wraps project->descriptor, item->package-ref,
      item->project-ref; contains discover-projects (glob *.csproj under sourceRoot,
      filtered to TitiPrefix-matching PackageIds to exclude non-repo projects)
- [ ] titi.config — load/validate titi.config.edn; apply defaults when file absent;
      E009 only when file is present but invalid EDN or fails schema
- [ ] titi.graph — ProjectGraph construction (via titi.interop); topological sort;
      convert ProjectGraphNodes to Clojure maps; pure data transformation only
- [ ] titi.swap — swap algorithm: Path/Combine for source lookup (not "/"
      concatenation); Kahn's algorithm + DFS back-edge detection for cycle check;
      partial swaps valid; returns SwapResult map
- [ ] titi.solution — .slnx generation via SolutionPersistence; includes only
      SwapResult.swapped closure (not full graph); atomic write (tmp → rename);
      creates .titi/solutions/ dir; guards against .titi existing as a file (E009)
- [ ] titi.core — entry point (ns titi.core (:gen-class :main true)); dispatch
      table; requires titi.interop first, then dynamically requires graph/solution;
      error key detection and structured stderr output; process exit codes

## Phase 1c — Commands (implement)

- [ ] titi open <packageId> — full pipeline: config → discover → graph → swap →
      solution → report SwapResult (swapped count, retained count, .slnx path)
- [ ] titi affected [--base <ref>] — git diff + graph → AffectedSet report;
      handle no-commits / shallow-clone: fall back to all-projects-affected with warning
- [ ] titi clean — rm -rf .titi/; print count of deleted files

## Phase 1d — Tests (implement alongside Phase 1b)

- [ ] Unit: titi.config — valid EDN loads; absent file returns defaults; invalid EDN
      returns E009 with location; schema violation returns E009 with field name
- [ ] Unit: titi.swap — swapped case; retained (no source) case; cycle detected case
      (Kahn fails → DFS identifies edge → retained with :cycle-prevention);
      partial transitive swap (B swapped, C retained)
- [ ] Unit: titi.solution — SolutionSpec built correctly from SwapResult;
      only swapped closure projects included (not full graph)
- [ ] Integration: `titi open Orion.Core.Data` against test/fixtures/sample-monorepo/
      — verify .slnx written, contains correct projects, InTitiContext=true in properties

## Phase 1e — Build & distribution (implement)

- [ ] Verify ClojureCLR.Next AOT/publish requirements (dotnet publish --self-contained
      or framework-dependent); do NOT assume DLR DLLs (Microsoft.Dynamic.dll etc.)
- [ ] Verify `just build` produces a runnable `titi` binary (Linux) / `titi.exe` (Windows)
- [ ] Add titi-specific justfile recipes: `run`, `titi-open` (smoke test against fixture)
- [ ] Update CI workflow to build and run tests against the sample-monorepo fixture

## Phase 2 — Deferred (next project)
- Graph cache (.titi/graph.cache)
- build-manifest / test-manifest (Traversal SDK)
- titi pkg, titi version, titi bundle
