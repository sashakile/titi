# Dependency Graph

## Purpose

The dependency graph capability builds and queries a full in-memory model of the monorepo's project inter-dependencies, supporting topological ordering, affected-set computation, and cycle detection.

## Requirements

### Requirement: Graph Construction

The system SHALL construct a `MonorepoGraph` by scanning all .csproj files under `repoRoot`, resolving package and project references, and producing a map of `GraphNode` entries keyed by canonical project path.

#### Scenario: Successful graph build
- **GIVEN** a monorepo with five projects having known inter-dependencies
- **WHEN** the graph is constructed
- **THEN** `MonorepoGraph.nodes` contains one entry per project, each with correct `dependencies` and `dependents` edges

#### Scenario: Repo root not found
- **WHEN** graph construction is attempted in a directory with no `.git` root
- **THEN** the system emits error E008 (GIT_NOT_AVAILABLE)

### Requirement: Topological Order

The system SHALL compute a stable topological sort of all `GraphNode` entries and store it as `MonorepoGraph.topologicalOrder`, such that every project appears after all its dependencies.

#### Scenario: Linear dependency chain
- **GIVEN** projects A → B → C
- **WHEN** the graph is sorted
- **THEN** topologicalOrder is [C, B, A] (leaves first)

#### Scenario: Diamond dependency
- **GIVEN** projects A → B, A → C, B → D, C → D
- **WHEN** the graph is sorted
- **THEN** D appears before both B and C, which appear before A

### Requirement: Graph Node Depth

The system SHALL assign each `GraphNode` a `depth` value equal to the length of the longest dependency path from that node to a leaf (zero for projects with no dependencies).

#### Scenario: Leaf node depth
- **GIVEN** a project with no dependencies
- **WHEN** the graph is built
- **THEN** its depth is 0

#### Scenario: Intermediate node depth
- **GIVEN** A → B → C where C is a leaf
- **WHEN** the graph is built
- **THEN** B has depth 1 and A has depth 2

### Requirement: Affected Set Computation

The system SHALL compute an `AffectedSet` from a set of changed files, identifying projects directly affected (own source changed) and transitively affected (depend on a directly affected project), and partitioning affected test projects into a `TieredTestSet`.

#### Scenario: Direct file change
- **GIVEN** a source file belonging to project X is modified
- **WHEN** `titi affected` is run
- **THEN** X appears in `directlyAffected`

#### Scenario: Transitive impact
- **GIVEN** project Y depends on project X and X is directly affected
- **WHEN** `titi affected` is run
- **THEN** Y appears in `transitivelyAffected`

#### Scenario: No changes
- **WHEN** git reports no changed files
- **THEN** `AffectedSet.directlyAffected` and `transitivelyAffected` are both empty

#### Scenario: Test tier assignment
- **GIVEN** an affected project that matches the `unit` glob in TestTierConfig
- **WHEN** the affected set is computed
- **THEN** that project appears in `TieredTestSet.unit`

#### Scenario: Shallow git clone fallback
- **GIVEN** the repository is a shallow git clone where the configured base commit is not available in the local history
- **WHEN** affected-set computation is attempted
- **THEN** the system emits a warning diagnostic explaining the base commit is unavailable, and returns an `AffectedSet` containing all discovered projects in `directlyAffected` as a full regression fallback

### Requirement: Cycle Detection

The system SHALL detect dependency cycles during graph construction and populate `CycleReport` entries describing each cycle, the edges forming it, and a diagnostic message.

#### Scenario: Cycle found
- **GIVEN** projects A → B → A form a cycle
- **WHEN** the graph is built
- **THEN** a `CycleReport` is emitted with code E002, listing [A, B, A] as the cycle

#### Scenario: Acyclic graph
- **GIVEN** a graph with no circular references
- **WHEN** the graph is built
- **THEN** no `CycleReport` entries are produced

### Requirement: AffectedSet and TieredTestSet Schemas

The system SHALL define the `AffectedSet` schema with fields: `changedFiles` (string[]), `directlyAffected` (ProjectDescriptor[]), `transitivelyAffected` (ProjectDescriptor[]), and `affectedTests` (TieredTestSet). The system SHALL define the `TieredTestSet` schema with fields: `unit` (ProjectDescriptor[]), `package` (ProjectDescriptor[]), `integration` (ProjectDescriptor[]), and `compatibility` (ProjectDescriptor[]).

#### Scenario: AffectedSet populated from changed files
- **GIVEN** a set of changed source files spanning two projects
- **WHEN** `titi affected` computes the affected set
- **THEN** the returned `AffectedSet` has `changedFiles` listing every modified file path, `directlyAffected` listing the two projects whose source changed, `transitivelyAffected` listing all downstream dependents, and `affectedTests` partitioned into the appropriate `TieredTestSet` tiers

#### Scenario: TieredTestSet populated by tier
- **GIVEN** the affected set includes one unit test project and one integration test project
- **WHEN** the `TieredTestSet` is constructed
- **THEN** the unit test project appears in `TieredTestSet.unit` and the integration test project appears in `TieredTestSet.integration`, with `TieredTestSet.package` and `TieredTestSet.compatibility` empty

### Requirement: Graph Fingerprinting

The system SHALL record a `FileFingerprint` (path, lastModified, sizeBytes, optional SHA-256 contentHash) for each .csproj and for global build files, storing them in `MonorepoGraph.fingerprints`.

#### Scenario: Fingerprint capture
- **WHEN** the graph is built
- **THEN** each .csproj under repoRoot has a corresponding fingerprint entry with at minimum path and lastModified populated
