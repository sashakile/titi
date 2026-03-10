**titi**

.NET Monorepo Orchestration CLI

**Architecture Document**

Schemas, Interfaces & Data Contracts

Status: DRAFT \| Version: 0.1

March 2026

**Table of Contents**

**1. Introduction**

This document defines the formal schemas, interfaces, and data contracts
for titi, a .NET monorepo orchestration CLI. It translates the concepts
from the Technical Design Document into concrete type definitions that
guide implementation. All schemas are expressed in a language-neutral
notation (TypeScript-style interfaces) with implementation notes for the
chosen ClojureCLR runtime.

Each section covers a distinct architectural boundary: the core domain
model, the CLI command surface, configuration contracts, the graph
subsystem, the reference-swap engine, build manifest generation, and the
cache layer.

**2. Core Domain Model**

The domain model captures the central abstractions titi operates on:
projects, references, the dependency graph, and solutions. All domain
types are immutable value objects; transformations produce new instances
rather than mutating in place.

**2.1 ProjectDescriptor**

Represents a single .NET project as discovered on disk and evaluated by
MSBuild. This is the primary node type in the dependency graph.

> interface ProjectDescriptor {
>
> /\*\* Absolute path to the .csproj file \*/
>
> path: string;
>
> /\*\* NuGet package identity (e.g. \"Orion.Core.Data\") \*/
>
> packageId: string;
>
> /\*\* Parsed version from \<Version\> property \*/
>
> version: SemanticVersion;
>
> /\*\* Target framework monikers (e.g. \[\"net8.0\",\"net9.0\"\]) \*/
>
> targetFrameworks: TFM\[\];
>
> /\*\* True if the project produces a NuGet package \*/
>
> isPackable: boolean;
>
> /\*\* True if the project is a test project \*/
>
> isTestProject: boolean;
>
> /\*\* Direct PackageReference dependencies \*/
>
> packageRefs: PackageRef\[\];
>
> /\*\* Direct ProjectReference dependencies \*/
>
> projectRefs: ProjectRef\[\];
>
> /\*\* Evaluated MSBuild properties (key-value) \*/
>
> properties: Record\<string, string\>;
>
> }

**2.2 Reference Types**

Two reference value types model the dual-mode dependency system at the
heart of titi.

> interface PackageRef {
>
> packageId: string;
>
> versionRange: string; // NuGet version range
>
> privateAssets: string \| null; // e.g. \"all\"
>
> excludeAssets: string \| null; // e.g. \"All\" when suppressed
>
> }
>
> interface ProjectRef {
>
> path: string; // Relative or absolute .csproj path
>
> isTransitive: boolean;
>
> }

**2.3 SemanticVersion**

A structured representation of a NuGet-compatible semantic version, used
in version mismatch policy evaluation.

> interface SemanticVersion {
>
> major: number;
>
> minor: number;
>
> patch: number;
>
> prerelease: string \| null; // e.g. \"alpha.1\"
>
> metadata: string \| null; // e.g. \"ci.12345\"
>
> }

**2.4 TFM (Target Framework Moniker)**

> interface TFM {
>
> moniker: string; // e.g. \"net9.0\"
>
> framework: \"net\" \| \"netstandard\" \| \"netcoreapp\";
>
> version: number; // e.g. 9.0, 2.1
>
> }

**2.5 ReferenceMode Enum**

Tracks the current resolution mode of a dependency edge.

> enum ReferenceMode {
>
> SOURCE = \"source\", // ProjectReference (local source)
>
> BINARY = \"binary\", // PackageReference (NuGet package)
>
> SUPPRESSED = \"suppressed\" // PackageRef with ExcludeAssets=All
>
> }

**3. Dependency Graph Schema**

The graph subsystem is the analytical core of titi. It wraps MSBuild\'s
ProjectGraph API and enriches the result with titi-specific metadata.

**3.1 MonorepoGraph**

The top-level graph container. Constructed once per session (or loaded
from cache) and passed immutably to all subsystems.

> interface MonorepoGraph {
>
> /\*\* All discovered projects indexed by absolute path \*/
>
> nodes: Map\<string, GraphNode\>;
>
> /\*\* Topologically sorted node keys (build order) \*/
>
> topologicalOrder: string\[\];
>
> /\*\* Repository root absolute path \*/
>
> repoRoot: string;
>
> /\*\* Timestamp of graph construction \*/
>
> builtAt: ISO8601Timestamp;
>
> /\*\* Source file checksums for invalidation \*/
>
> fingerprints: Map\<string, string\>;
>
> }

**3.2 GraphNode**

A single node in the dependency DAG, wrapping a ProjectDescriptor with
graph-edge metadata.

> interface GraphNode {
>
> project: ProjectDescriptor;
>
> /\*\* Direct upstream dependencies (this project depends on) \*/
>
> dependencies: GraphEdge\[\];
>
> /\*\* Direct downstream consumers (depend on this project) \*/
>
> dependents: GraphEdge\[\];
>
> /\*\* Depth from closest entry point (0 = entry point) \*/
>
> depth: number;
>
> }

**3.3 GraphEdge**

A directed edge between two graph nodes, carrying reference-mode
metadata.

> interface GraphEdge {
>
> /\*\* Source node path (the dependent) \*/
>
> from: string;
>
> /\*\* Target node path (the dependency) \*/
>
> to: string;
>
> /\*\* Current resolution mode \*/
>
> mode: ReferenceMode;
>
> /\*\* Original version constraint from PackageReference \*/
>
> versionRange: string \| null;
>
> /\*\* Whether this edge is transitive-only \*/
>
> isTransitive: boolean;
>
> }

**3.4 AffectedSet**

Returned by the impact analysis subsystem. Captures the projects
affected by a set of file changes.

> interface AffectedSet {
>
> /\*\* The files that changed (git diff) \*/
>
> changedFiles: string\[\];
>
> /\*\* Projects directly containing changed files \*/
>
> directlyAffected: ProjectDescriptor\[\];
>
> /\*\* Transitive downstream consumers \*/
>
> transitivelyAffected: ProjectDescriptor\[\];
>
> /\*\* Affected test projects, tiered \*/
>
> affectedTests: TieredTestSet;
>
> }

**3.5 TieredTestSet**

Groups affected test projects by execution tier, enabling incremental
test strategies on CI.

> interface TieredTestSet {
>
> unit: ProjectDescriptor\[\];
>
> package: ProjectDescriptor\[\];
>
> integration: ProjectDescriptor\[\];
>
> compatibility: ProjectDescriptor\[\];
>
> }

**3.6 CycleReport**

Produced by the topological sort when a reference swap would introduce a
circular dependency.

> interface CycleReport {
>
> /\*\* Ordered list of nodes forming the cycle \*/
>
> cycle: string\[\];
>
> /\*\* Edges that should remain in BINARY mode \*/
>
> edgesToPreserve: GraphEdge\[\];
>
> /\*\* Human-readable diagnostic message \*/
>
> diagnostic: string;
>
> }

**4. Reference Swap Engine Interfaces**

The swap engine is the mechanism that converts PackageReferences to
ProjectReferences (and vice versa) without modifying original .csproj
files.

**4.1 SwapRequest**

Input to the swap engine, specifying which projects to activate in
Source Mode.

> interface SwapRequest {
>
> /\*\* Projects to open in Source Mode \*/
>
> targets: string\[\]; // package IDs or paths
>
> /\*\* Version mismatch policy \*/
>
> versionPolicy: VersionPolicy;
>
> /\*\* Include transitive dependencies? \*/
>
> includeTransitive: boolean;
>
> /\*\* Override version checks \*/
>
> force: boolean;
>
> }
>
> enum VersionPolicy {
>
> STRICT = \"strict\",
>
> SEMVER_COMPATIBLE = \"semver-compatible\",
>
> FORCE = \"force\"
>
> }

**4.2 SwapResult**

Output of the swap engine describing which references were swapped,
which were kept in Binary Mode, and any diagnostics.

> interface SwapResult {
>
> /\*\* Successfully swapped to Source Mode \*/
>
> swapped: SwappedRef\[\];
>
> /\*\* Kept in Binary Mode with reason \*/
>
> retained: RetainedRef\[\];
>
> /\*\* Cycle reports, if any \*/
>
> cycles: CycleReport\[\];
>
> /\*\* Generated MSBuild properties \*/
>
> msbuildContext: MSBuildContext;
>
> }
>
> interface SwappedRef {
>
> packageId: string;
>
> fromVersion: string;
>
> localSourcePath: string;
>
> localVersion: SemanticVersion;
>
> consumers: string\[\]; // project paths consuming this
>
> }
>
> interface RetainedRef {
>
> packageId: string;
>
> reason: RetainedReason;
>
> detail: string;
>
> }
>
> enum RetainedReason {
>
> NO_LOCAL_SOURCE = \"no-local-source\",
>
> VERSION_MISMATCH = \"version-mismatch\",
>
> TFM_INCOMPATIBLE = \"tfm-incompatible\",
>
> CYCLE_PREVENTION = \"cycle-prevention\"
>
> }

**4.3 MSBuildContext**

The set of MSBuild properties and environment state that titi injects
when launching a Source Mode build.

> interface MSBuildContext {
>
> /\*\* Set to \"true\" to activate swap logic in .targets \*/
>
> inTitiContext: \"true\" \| \"false\";
>
> /\*\* Package naming prefix for dynamic matching \*/
>
> titiPrefix: string;
>
> /\*\* Root path for source project lookup \*/
>
> titiSourceRoot: string;
>
> /\*\* Additional properties passed via /p: \*/
>
> additionalProps: Record\<string, string\>;
>
> }

**5. Configuration Schemas**

titi uses a layered configuration model: repository-level defaults live
in titi.config.edn at the repo root, with per-command overrides via CLI
flags. The schema below uses EDN (Extensible Data Notation), the native
serialization format for Clojure.

**5.1 TitiConfig (titi.config.edn)**

> interface TitiConfig {
>
> /\*\* Package naming prefix for swap discovery \*/
>
> prefix: string; // e.g. \"Orion.\"
>
> /\*\* Root directory containing source projects \*/
>
> sourceRoot: string; // e.g. \"src/\"
>
> /\*\* Default version mismatch policy \*/
>
> versionPolicy: VersionPolicy;
>
> /\*\* Graph cache settings \*/
>
> cache: CacheConfig;
>
> /\*\* Test tier classification rules \*/
>
> testTiers: TestTierConfig;
>
> /\*\* IDE launch configuration \*/
>
> ide: IdeConfig;
>
> /\*\* CI-specific overrides \*/
>
> ci: CiConfig;
>
> }

**5.2 CacheConfig**

> interface CacheConfig {
>
> /\*\* Enable/disable graph caching \*/
>
> enabled: boolean;
>
> /\*\* Cache directory (default: \".titi/\") \*/
>
> directory: string;
>
> /\*\* Max cache age before forced rebuild (seconds) \*/
>
> maxAge: number;
>
> /\*\* Files that trigger full cache invalidation \*/
>
> globalTriggers: string\[\];
>
> // e.g. \[\"Directory.Build.props\", \"Directory.Build.targets\",
>
> // \"Directory.Packages.props\"\]
>
> }

**5.3 TestTierConfig**

Defines rules for classifying test projects into execution tiers.

> interface TestTierConfig {
>
> /\*\* Glob patterns for each test tier \*/
>
> unit: string\[\]; // e.g. \[\"\*\*/\*.UnitTests.csproj\"\]
>
> package: string\[\]; // e.g. \[\"\*\*/\*.PackageTests.csproj\"\]
>
> integration: string\[\]; // e.g.
> \[\"\*\*/\*.IntegrationTests.csproj\"\]
>
> compatibility: string\[\]; // e.g. \[\"\*\*/\*.CompatTests.csproj\"\]
>
> /\*\* Fallback tier for unmatched test projects \*/
>
> defaultTier: \"unit\" \| \"integration\";
>
> }

**5.4 IdeConfig**

> interface IdeConfig {
>
> /\*\* Command to open a .slnx file \*/
>
> launchCommand: string; // e.g. \"rider\", \"code\", \"devenv\"
>
> /\*\* Additional arguments \*/
>
> args: string\[\];
>
> /\*\* Auto-open after solution generation \*/
>
> autoOpen: boolean;
>
> }

**5.5 CiConfig**

> interface CiConfig {
>
> /\*\* Branch patterns for full regression \*/
>
> fullRegressionBranches: string\[\]; // e.g. \[\"main\",
> \"release/\*\"\]
>
> /\*\* Max parallel build jobs \*/
>
> maxParallelism: number;
>
> /\*\* Output format for affected/manifest commands \*/
>
> outputFormat: \"json\" \| \"text\" \| \"github-actions\";
>
> }

**6. CLI Command Interfaces**

Each CLI command has a well-defined input (arguments and flags) and
output (structured result). Below are the interfaces for every command
across all delivery phases.

**6.1 Command Surface Overview**

  -------------------------- ----------- ---------------------------------------------
  **Command**                **Phase**   **Description**

  titi open \<project\>      1           Generate transient .slnx with reference
                                         swapping and open IDE

  titi affected              1           List projects affected by current git changes

  titi clean                 1           Remove all titi-generated artifacts

  titi cache warm            1           Pre-build the full dependency graph

  titi build-manifest        2           Generate Traversal .proj for affected change
                                         set

  titi test-manifest         2           Generate Traversal .proj for affected test
                                         suites

  titi pkg                   2           Manage packages in Directory.Packages.props
  \<add\|remove\|upgrade\>

  titi check \<package\>     3           Forward Flow compatibility check against
                                         consumers

  titi audit                 3           Transitive dependency audit with ownership
                                         mapping

  titi repl                  3           Interactive Build REPL for graph exploration
  -------------------------- ----------- ---------------------------------------------

**6.2 titi open**

> interface OpenCommand {
>
> input: {
>
> target: string; // Package ID or .csproj path
>
> versionPolicy?: VersionPolicy; // default: from config
>
> includeTransitive?: boolean; // default: true
>
> force?: boolean; // default: false
>
> noLaunch?: boolean; // generate only, don\'t open IDE
>
> };
>
> output: {
>
> solutionPath: string; // generated .slnx path
>
> swapResult: SwapResult;
>
> projectCount: number;
>
> launchedIde: boolean;
>
> };
>
> }

**6.3 titi affected**

> interface AffectedCommand {
>
> input: {
>
> base?: string; // git ref (default: HEAD\~1)
>
> head?: string; // git ref (default: working tree)
>
> format?: \"json\" \| \"text\" \| \"github-actions\";
>
> includeTests?: boolean; // default: true
>
> };
>
> output: {
>
> affected: AffectedSet;
>
> format: string;
>
> };
>
> }

**6.4 titi build-manifest / test-manifest**

> interface ManifestCommand {
>
> input: {
>
> base?: string;
>
> head?: string;
>
> outputPath?: string; // default: .titi/manifests/
>
> tier?: TestTier; // test-manifest only
>
> };
>
> output: {
>
> manifestPath: string; // generated .proj path
>
> includedProjects: string\[\];
>
> estimatedDuration: number; // seconds, if historical data available
>
> };
>
> }

**6.5 titi check (Forward Flow)**

> interface CheckCommand {
>
> input: {
>
> package: string; // package ID to validate
>
> branch?: string; // source branch (default: current)
>
> consumers?: string\[\]; // specific consumers (default: all)
>
> };
>
> output: {
>
> package: string;
>
> localVersion: SemanticVersion;
>
> results: ConsumerCheckResult\[\];
>
> allPassed: boolean;
>
> };
>
> }
>
> interface ConsumerCheckResult {
>
> consumer: string;
>
> buildSuccess: boolean;
>
> testSuccess: boolean;
>
> errors: CompilationError\[\];
>
> }
>
> interface CompilationError {
>
> file: string;
>
> line: number;
>
> column: number;
>
> code: string; // e.g. \"CS0117\"
>
> message: string;
>
> }

**6.6 titi pkg (Package Management)**

> interface PkgCommand {
>
> input: {
>
> action: \"add\" \| \"remove\" \| \"upgrade\";
>
> packageId: string;
>
> version?: string; // required for add/upgrade
>
> projects?: string\[\]; // scope (default: all)
>
> };
>
> output: {
>
> modifiedFile: string; // Directory.Packages.props path
>
> action: string;
>
> packageId: string;
>
> previousVersion: string \| null;
>
> newVersion: string \| null;
>
> affectedProjects: string\[\];
>
> };
>
> }

**7. Build Manifest Schema**

Build Manifests are Traversal .proj files generated by titi for targeted
CI builds. The schema below describes the in-memory representation
before serialization to MSBuild XML.

**7.1 BuildManifest**

> interface BuildManifest {
>
> /\*\* Unique identifier for this manifest \*/
>
> id: string;
>
> /\*\* Generation timestamp \*/
>
> generatedAt: ISO8601Timestamp;
>
> /\*\* Git refs used to compute the affected set \*/
>
> gitContext: {
>
> base: string;
>
> head: string;
>
> branch: string;
>
> };
>
> /\*\* Projects included in this manifest \*/
>
> projects: ManifestEntry\[\];
>
> /\*\* MSBuild SDK for the Traversal project \*/
>
> sdk: \"Microsoft.Build.Traversal/4.1.0\";
>
> }
>
> interface ManifestEntry {
>
> projectPath: string; // relative to repo root
>
> reason: \"directly-affected\" \| \"transitive-dependency\" \|
> \"test-coverage\";
>
> tier: TestTier \| null;
>
> }

**7.2 Traversal .proj Output Format**

The generated .proj file follows the Microsoft.Build.Traversal SDK
conventions:

> \<!\-- Generated by titi - do not edit \--\>
>
> \<Project Sdk=\"Microsoft.Build.Traversal/4.1.0\"\>
>
> \<ItemGroup\>
>
> \<ProjectReference Include=\"../src/Orion.Core.Data/..csproj\" /\>
>
> \<ProjectReference Include=\"../src/Orion.Auth/..csproj\" /\>
>
> \<ProjectReference Include=\"../tests/Orion.Core.Data.Tests/..csproj\"
> /\>
>
> \</ItemGroup\>
>
> \</Project\>

**8. Solution Generation Schema**

titi generates transient .slnx solution files scoped to a developer\'s
task. The schema defines the in-memory model used by the
SolutionPersistence library.

**8.1 SolutionSpec**

> interface SolutionSpec {
>
> /\*\* Solution file format \*/
>
> format: \"slnx\" \| \"sln\";
>
> /\*\* Output path (within .titi/solutions/) \*/
>
> outputPath: string;
>
> /\*\* Projects to include \*/
>
> projects: SolutionProjectEntry\[\];
>
> /\*\* Solution folders for logical grouping \*/
>
> folders: SolutionFolder\[\];
>
> /\*\* Global properties to set \*/
>
> globalProperties: Record\<string, string\>;
>
> }
>
> interface SolutionProjectEntry {
>
> path: string;
>
> projectGuid: string;
>
> displayName: string;
>
> folderPath: string \| null; // parent solution folder
>
> }
>
> interface SolutionFolder {
>
> name: string;
>
> guid: string;
>
> parentFolder: string \| null;
>
> }

**9. Graph Cache Schema**

The cache layer persists the evaluated dependency graph to disk,
enabling sub-second startup for repeated commands. The cache is stored
as a binary EDN file at .titi/graph.cache.

**9.1 GraphCache**

> interface GraphCache {
>
> /\*\* Cache format version (for migration) \*/
>
> schemaVersion: number;
>
> /\*\* Full serialized MonorepoGraph \*/
>
> graph: MonorepoGraph;
>
> /\*\* File fingerprints at time of construction \*/
>
> fingerprints: Map\<string, FileFingerprint\>;
>
> /\*\* titi version that built this cache \*/
>
> titiVersion: string;
>
> }
>
> interface FileFingerprint {
>
> path: string;
>
> lastModified: ISO8601Timestamp;
>
> sizeBytes: number;
>
> contentHash: string \| null; // SHA-256, optional
>
> }

**9.2 Cache Invalidation Rules**

The cache uses a tiered invalidation strategy to balance freshness with
startup speed.

  -------------------------- ------------- ----------------------------------
  **Trigger**                **Scope**     **Action**

  Any .csproj modified       Subgraph      Re-evaluate changed project and
                                           its dependents

  Directory.Build.props      Full graph    Invalidate entire cache; rebuild
  changed                                  from scratch

  Directory.Build.targets    Full graph    Invalidate entire cache; rebuild
  changed                                  from scratch

  Directory.Packages.props   Full graph    Invalidate entire cache (version
  changed                                  changes may alter graph)

  titi version changed       Full graph    Invalidate entire cache (schema
                                           may have changed)

  Cache age \> maxAge        Full graph    Invalidate entire cache as a
                                           safety net
  -------------------------- ------------- ----------------------------------

**10. MSBuild Integration Contracts**

titi\'s reference swapping mechanism relies on two MSBuild import files
that are committed to the repository. These contracts are
version-controlled and shared by all developers.

**10.1 Directory.Build.props Additions**

Properties injected by titi into the repository\'s existing
Directory.Build.props:

> \<PropertyGroup Label=\"titi Configuration\"\>
>
> \<!\-- Package naming prefix for swap matching \--\>
>
> \<TitiPrefix\>Orion.\</TitiPrefix\>
>
> \<!\-- Root path for source project discovery \--\>
>
> \<TitiSourceRoot\>\$(MSBuildThisFileDirectory)src\\\</TitiSourceRoot\>
>
> \<!\-- Activation flag (false by default; set true by titi) \--\>
>
> \<InTitiContext Condition=\"\'\$(InTitiContext)\' ==
> \'\'\"\>false\</InTitiContext\>
>
> \<!\-- Enable VS Build Acceleration when in IDE \--\>
>
> \<AccelerateBuildsInVisualStudio\>true\</AccelerateBuildsInVisualStudio\>
>
> \</PropertyGroup\>

**10.2 Directory.Build.targets Swap Logic**

The core swap mechanism, evaluated after all .csproj PackageReferences
are declared:

> \<Project\>
>
> \<ItemGroup Condition=\"\'\$(InTitiContext)\' == \'true\'\"\>
>
> \<!\-- Discover swappable packages by prefix + local source \--\>
>
> \<\_TitiSwappable
>
> Include=\"@(PackageReference)\"
>
> Condition=\"\$(\[System.String\]::new(\'%(Identity)\')
>
> .StartsWith(\'\$(TitiPrefix)\'))
>
> And Exists(\'\$(TitiSourceRoot)%(Identity)\\%(Identity).csproj\')\"
>
> /\>
>
> \<!\-- Suppress matched packages (retain in NuGet graph) \--\>
>
> \<PackageReference Update=\"@(\_TitiSwappable)\"
>
> ExcludeAssets=\"All\" /\>
>
> \<!\-- Inject ProjectReferences dynamically \--\>
>
> \<ProjectReference
>
> Include=\"\$(TitiSourceRoot)%(\_TitiSwappable.Identity)
>
> \\%(\_TitiSwappable.Identity).csproj\" /\>
>
> \</ItemGroup\>
>
> \</Project\>

**10.3 Property Contract**

The following MSBuild properties form the public contract between titi
and the build system. Changes to these properties are considered
breaking changes.

  ------------------------------------ ---------- ----------- ---------------------------------
  **Property**                         **Type**   **Set By**  **Description**

  \$(InTitiContext)                    bool       titi CLI    Activates swap logic; false by
                                                              default

  \$(TitiPrefix)                       string     Repo config NuGet package ID prefix for
                                                              matching

  \$(TitiSourceRoot)                   path       Repo config Root directory for source project
                                                              lookup

  \$(AccelerateBuildsInVisualStudio)   bool       Repo config Enables VS FUTDC optimization
  ------------------------------------ ---------- ----------- ---------------------------------

**11. File System Layout**

titi creates and manages files exclusively within the .titi/ directory
(gitignored). No files outside this directory are created or modified by
titi at runtime.

> repo-root/
>
> .titi/ \# titi workspace (gitignored)
>
> graph.cache \# serialized MonorepoGraph
>
> solutions/ \# generated .slnx files
>
> Orion.Core.Data.slnx
>
> Orion.Auth.slnx
>
> manifests/ \# generated Traversal .proj files
>
> build-manifest-abc123.proj
>
> test-manifest-abc123.proj
>
> logs/ \# diagnostic logs
>
> titi-2026-03-04.log
>
> titi.config.edn \# repository configuration
>
> Directory.Build.props \# contains TitiPrefix, TitiSourceRoot
>
> Directory.Build.targets \# contains swap logic
>
> Directory.Packages.props \# CPM version management

**12. Error and Diagnostic Schemas**

**12.1 TitiError**

All titi commands return structured errors enabling programmatic
handling in CI.

> interface TitiError {
>
> code: ErrorCode;
>
> message: string;
>
> context: {
>
> command: string;
>
> target: string \| null;
>
> phase: \"graph-build\" \| \"swap\" \| \"solution-gen\"
>
> \| \"manifest-gen\" \| \"build\" \| \"test\";
>
> };
>
> suggestions: string\[\];
>
> }
>
> enum ErrorCode {
>
> GRAPH_BUILD_FAILED = \"E001\",
>
> CYCLE_DETECTED = \"E002\",
>
> VERSION_MISMATCH = \"E003\",
>
> TFM_INCOMPATIBLE = \"E004\",
>
> NO_LOCAL_SOURCE = \"E005\",
>
> CACHE_CORRUPT = \"E006\",
>
> MSBUILD_NOT_FOUND = \"E007\",
>
> GIT_NOT_AVAILABLE = \"E008\",
>
> CONFIG_INVALID = \"E009\",
>
> BUILD_FAILED = \"E010\",
>
> TEST_FAILED = \"E011\",
>
> }

**12.2 DiagnosticEvent**

Structured log entries emitted during command execution for
observability.

> interface DiagnosticEvent {
>
> timestamp: ISO8601Timestamp;
>
> level: \"debug\" \| \"info\" \| \"warn\" \| \"error\";
>
> source: string; // subsystem name
>
> message: string;
>
> data: Record\<string, any\> \| null;
>
> durationMs: number \| null;
>
> }

**13. ClojureCLR Implementation Notes**

The schemas above are expressed in TypeScript notation for readability.
In the ClojureCLR implementation, these map to persistent data
structures.

**13.1 Type Mapping**

  ------------------ -------------------------- -------------------------
  **Schema Type**    **ClojureCLR               **Notes**
                     Representation**

  interface          Map (hash-map)             Keyword keys, e.g.
                                                :package-id

  enum               Keyword set                e.g. :source, :binary,
                                                :suppressed

  string\[\]         Vector                     Persistent vector of
                                                strings

  Map\<K,V\>         Map                        Persistent hash-map

  Record\<K,V\>      Map                        Same as Map in Clojure

  number             Long or Double             Context-dependent

  boolean            Boolean                    true / false literals

  null               nil                        Clojure nil
  ------------------ -------------------------- -------------------------

**13.2 Spec Validation**

Each schema will have a corresponding clojure.spec definition for
runtime validation. Specs are registered in the titi.spec namespace and
used at system boundaries (config loading, CLI input parsing, cache
deserialization).

> ;; Example spec for ProjectDescriptor
>
> (s/def ::path string?)
>
> (s/def ::package-id string?)
>
> (s/def ::version ::semantic-version)
>
> (s/def ::target-frameworks (s/coll-of ::tfm :kind vector?))
>
> (s/def ::is-packable boolean?)
>
> (s/def ::is-test-project boolean?)
>
> (s/def ::project-descriptor
>
> (s/keys :req-un \[::path ::package-id ::version
>
> ::target-frameworks ::is-packable
>
> ::is-test-project ::package-refs
>
> ::project-refs ::properties\]))

**13.3 .NET Interop Boundaries**

ClojureCLR interop with MSBuild APIs occurs at well-defined boundaries.
Data crosses the interop boundary as .NET objects and is immediately
converted to persistent Clojure maps. All internal processing uses
Clojure data structures exclusively.

> ;; Interop boundary: .NET -\> Clojure
>
> (defn project-\>descriptor \[\^Project msbuild-project\]
>
> {:path (.FullPath msbuild-project)
>
> :package-id (.GetPropertyValue msbuild-project \"PackageId\")
>
> :version (parse-version
>
> (.GetPropertyValue msbuild-project \"Version\"))
>
> :target-frameworks (parse-tfms
>
> (.GetPropertyValue msbuild-project \"TargetFrameworks\"))
>
> :package-refs (mapv item-\>package-ref
>
> (.GetItems msbuild-project \"PackageReference\"))
>
> :project-refs (mapv item-\>project-ref
>
> (.GetItems msbuild-project \"ProjectReference\"))})

**Appendix A: Full Type Index**

Quick-reference index of all schemas and interfaces defined in this
document.

  ------------------------ ------------- ----------------------------------
  **Type**                 **Section**   **Category**

  ProjectDescriptor        2.1           Domain Model

  PackageRef / ProjectRef  2.2           Domain Model

  SemanticVersion          2.3           Domain Model

  TFM                      2.4           Domain Model

  ReferenceMode            2.5           Domain Model

  MonorepoGraph            3.1           Graph

  GraphNode                3.2           Graph

  GraphEdge                3.3           Graph

  AffectedSet              3.4           Graph

  TieredTestSet            3.5           Graph

  CycleReport              3.6           Graph

  SwapRequest / SwapResult 4.1--4.2      Swap Engine

  MSBuildContext           4.3           Swap Engine

  TitiConfig               5.1           Configuration

  CacheConfig              5.2           Configuration

  TestTierConfig           5.3           Configuration

  IdeConfig / CiConfig     5.4--5.5      Configuration

  OpenCommand              6.2           CLI

  AffectedCommand          6.3           CLI

  ManifestCommand          6.4           CLI

  CheckCommand             6.5           CLI

  PkgCommand               6.6           CLI

  BuildManifest /          7.1           Build Manifest
  ManifestEntry

  SolutionSpec             8.1           Solution Gen

  GraphCache /             9.1           Cache
  FileFingerprint

  TitiError / ErrorCode    12.1          Diagnostics

  DiagnosticEvent          12.2          Diagnostics
  ------------------------ ------------- ----------------------------------
