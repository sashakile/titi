Tracer-bullet architecture decisions:

1. ENTRY POINT NAMESPACE
   (ns titi.core (:gen-class :main true))
   Dispatch table: commands → handler fns
   Environment: titi.interop MUST be the first namespace required. RegisterDefaults
   must fire before any other namespace that references MSBuild types is loaded.
   Use dynamic require for titi.graph and titi.solution to guarantee ordering.

2. INTEROP BOUNDARY RULE
   .NET objects cross the boundary in, get converted to Clojure maps immediately.
   All internal processing uses persistent Clojure data structures.
   Output converts maps back to .NET API calls at the shell layer.
   Pattern: project->descriptor, item->package-ref, item->project-ref

   Shell namespaces (allowed to call .NET APIs directly):
     titi.interop  — MSBuildLocator setup, ProjectGraph construction
     titi.solution — SolutionPersistence file writing
     titi.core     — CLI I/O, process exit
   Core namespaces (pure Clojure maps, no .NET API calls):
     titi.config   — EDN loading and validation
     titi.graph    — graph data structure transformation (receives converted maps)
     titi.swap     — swap algorithm (operates on Clojure maps only)

3. GRAPH CONSTRUCTION (titi.interop + titi.graph)
   Step a — Discovery (titi.interop):
     Glob for *.csproj under sourceRoot using System.IO.Directory/EnumerateFiles.
     Exclude projects whose PackageId does not start with TitiPrefix (prevents
     titi's own CLI project from entering the graph when running inside the titi repo).
   Step b — Evaluation (titi.interop):
     Pass discovered paths as entry-points to (ProjectGraph. entry-paths).
   Step c — Conversion (titi.graph):
     Convert each ProjectGraphNode to a GraphNode Clojure map.
     Keys: :path :project (ProjectDescriptor map) :dependencies :dependents :depth
     Topological order from ProjectGraph/ProjectNodesTopologicallySorted.

4. SWAP ENGINE (titi.swap)
   Input: SwapRequest map {:targets [...] :version-policy :semver-compatible :include-transitive true}
   Algorithm:
     a. For each target packageId, check if local .csproj exists at:
        (Path/Combine source-root package-id (str package-id ".csproj"))
        Use System.IO.Path/Combine — NOT string "/" concatenation (breaks on Windows).
     b. If yes → add to :swapped; if no → add to :retained with :no-local-source
     c. Cycle check using Kahn's algorithm on the augmented graph (swap edges added):
        - Model proposed swaps as new directed edges in the graph
        - Run Kahn's algorithm; if it cannot produce a full topological order,
          a cycle exists
        - Use DFS back-edge detection to identify the minimal edge set causing
          the cycle; retain those specific targets with :cycle-prevention
        - Partial swaps are valid: swap the safe targets, retain the cyclic ones
     d. Build MSBuildContext: {:in-titi-context "true" :titi-prefix prefix :titi-source-root source-root}
   Retain PackageRef with ExcludeAssets=All (do NOT remove it); add ProjectRef alongside.

5. SOLUTION GENERATION (titi.solution)
   Use Microsoft.VisualStudio.SolutionPersistence.
   Output: .titi/solutions/<packageId>.slnx
   Include: target project + projects in SwapResult.swapped (the computed swap
   closure) — NOT the entire transitive ProjectRef graph of the repo.
   Set global properties: InTitiContext=true, TitiPrefix, TitiSourceRoot.
   Write atomically: write to .titi/solutions/<packageId>.slnx.tmp then rename.
   If .titi/ exists as a non-directory file: emit E009 CONFIG_INVALID
   "expected .titi/ to be a directory" and abort.

6. CONFIG (titi.config)
   Load titi.config.edn from repo root using clojure.edn/read.
   Schema: {:prefix "Company." :source-root "src/" :version-policy :semver-compatible ...}
   Missing file → apply all defaults and continue (NOT an error).
   Present but unparseable (invalid EDN) → E009 with parse error location and abort.
   Present but fails schema validation → E009 with field name and expected type.
   Defaults: {:prefix "" :source-root "src/" :version-policy :semver-compatible
              :cache {:enabled true :directory ".titi/" :max-age 3600
                      :global-triggers ["Directory.Build.props"
                                        "Directory.Build.targets"
                                        "Directory.Packages.props"]}
              :ide {:auto-open false}}

7. FILESYSTEM LAYOUT
   .titi/ directory is the artifact root (gitignored).
   titi creates .titi/ on first run if not present.
   If .titi exists as a non-directory file: E009 CONFIG_INVALID.
   solutions/  — generated .slnx files
   graph.cache — serialised MonorepoGraph (Phase 2, not in tracer-bullet)

8. ERROR HANDLING
   All errors return {:error {:code :E001 :message "..." :context {...} :suggestions [...]}}
   CLI shell checks for :error key; prints structured message and exits non-zero.
   E001 GRAPH_BUILD_FAILED  — ProjectGraph construction or .csproj parse failure
   E005 NO_LOCAL_SOURCE     — swap retained reason (package has no local source)
   E007 MSBUILD_NOT_FOUND   — MSBuildLocator.RegisterDefaults() found no installation
   E009 CONFIG_INVALID      — present config is unparseable or fails schema validation

9. AOT COMPILATION (ClojureCLR.Next)
   (:gen-class :main true) on titi.core namespace.
   ClojureCLR.Next uses the standard .NET hosting model — NOT the DLR.
   Do NOT bundle Microsoft.Dynamic.dll or Microsoft.Scripting.dll (those are
   ClojureCLR classic / DLR assemblies; ClojureCLR.Next does not use them).
   Distribution: dotnet publish --self-contained -r <rid> or framework-dependent
   publish alongside the .NET runtime. Verify exact requirements against
   ClojureCLR.Next documentation before implementing.
