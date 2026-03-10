A tracer-bullet for titi should exercise the full vertical slice of the CLI end-to-end: graph construction via MSBuild.Graph, one PackageReference→ProjectReference swap, and generating a transient .slnx solution file. This proves the ClojureCLR/.NET interop works, MSBuildLocator initialises correctly, and the core swap mechanism produces valid MSBuild output before any other feature is built.

Key constraints from architecture docs:
- MSBuildLocator.RegisterDefaults() must be called before ANY MSBuild type is referenced
- Swap mechanism: retain PackageRef with ExcludeAssets=All + inject ProjectRef (not replace)
- InTitiContext=true activates swap logic in Directory.Build.targets
- TitiPrefix and TitiSourceRoot are the naming convention contract
- .slnx output via Microsoft.VisualStudio.SolutionPersistence
- Config from titi.config.edn (prefix, sourceRoot, versionPolicy); missing config applies defaults (not an error)

Scope for tracer-bullet (Phase 1 only):
1. titi open <packageId> — the single command that ties everything together
2. Must: load config, build graph, compute swap, generate .slnx, report result
3. titi affected — secondary target (same graph, different output)
4. titi clean — trivial (rm .titi/)

What to skip in tracer-bullet:
- titi cache warm (deferred — no graph cache in Phase 1)
- Versioning / bundle commands (Phase 2+)
- titi check / audit / repl (Phase 3)
- Full CPM lock-file integration
- NBGV integration
- CI Traversal manifest generation

Implementation language: ClojureCLR.Next (AOT compiled, :gen-class :main true)
Key NuGet packages needed:
- Microsoft.Build.Locator (for RegisterDefaults — must be referenced before Microsoft.Build)
- Microsoft.Build (contains ProjectGraph, Project, evaluation APIs)
- Microsoft.Build.Framework (MSBuild event types)
- Microsoft.VisualStudio.SolutionPersistence (for .slnx serialisation)
- Clojure (ClojureCLR.Next runtime)
