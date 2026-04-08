# Dependency Graph

## Purpose

The dependency graph capability builds and queries a full in-memory model of the monorepo's project inter-dependencies, supporting topological ordering, affected-set computation, and cycle detection.

## Requirements

### Requirement DG-01: Graph Construction

The system SHALL construct a `MonorepoGraph` by scanning all .csproj files under `repoRoot`, resolving package and project references, and producing a map of `GraphNode` entries keyed by canonical project path.

#### Scenario: Successful graph build
- **GIVEN** a monorepo with five projects having known inter-dependencies
- **WHEN** the graph is constructed
- **THEN** `MonorepoGraph.nodes` contains one entry per project, each with correct `dependencies` and `dependents` edges

#### Scenario: Repo root not found
- **WHEN** graph construction is attempted in a directory with no `.git` root
- **THEN** the system emits error E008 (GIT_NOT_AVAILABLE)

### Requirement DG-02: Topological Order

The system SHALL compute a stable topological sort of all `GraphNode` entries and store it as `MonorepoGraph.topologicalOrder`, such that every project appears after all its dependencies.

> **Invariant — Stability:** When the dependency graph is unchanged between invocations, `topologicalOrder` SHALL be identical. Ties among nodes with no mutual dependency SHALL be broken by lexicographic order of canonical project path.

> **Invariant — Node Preservation:** `|topologicalOrder| = |MonorepoGraph.nodes|` — the sort SHALL neither drop nor duplicate any graph node.

#### Scenario: Linear dependency chain
- **GIVEN** projects A → B → C
- **WHEN** the graph is sorted
- **THEN** topologicalOrder is [C, B, A] (leaves first)

#### Scenario: Diamond dependency
- **GIVEN** projects A → B, A → C, B → D, C → D
- **WHEN** the graph is sorted
- **THEN** D appears before both B and C, which appear before A

### Requirement DG-03: Graph Node Depth

The system SHALL assign each `GraphNode` a `depth` value equal to the length of the longest dependency path from that node to a leaf (zero for projects with no dependencies).

#### Scenario: Leaf node depth
- **GIVEN** a project with no dependencies
- **WHEN** the graph is built
- **THEN** its depth is 0

#### Scenario: Intermediate node depth
- **GIVEN** A → B → C where C is a leaf
- **WHEN** the graph is built
- **THEN** B has depth 1 and A has depth 2

### Requirement DG-04: Affected Set Computation

The system SHALL compute an `AffectedSet` from a set of changed files, identifying projects directly affected (own source changed) and transitively affected (depend on a directly affected project), and partitioning affected test projects into a `TieredTestSet`. The `directlyAffected` and `transitivelyAffected` sets SHALL be mutually exclusive: a project that qualifies as both (own source changed AND depends on another directly affected project) SHALL appear only in `directlyAffected`.

> **Invariant — Mutual Exclusion:** `directlyAffected ∩ transitivelyAffected = ∅`.

> **Invariant — Completeness:** For every project P in the graph, if P is reachable via dependency edges from any project in `directlyAffected`, then P SHALL appear in either `directlyAffected` or `transitivelyAffected`. No downstream dependent of a directly affected project shall be omitted.

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

### Requirement DG-05: Cycle Detection

The system SHALL detect dependency cycles during graph construction and populate `CycleReport` entries describing each cycle, the edges forming it, and a diagnostic message.

#### Scenario: Cycle found
- **GIVEN** projects A → B → A form a cycle
- **WHEN** the graph is built
- **THEN** a `CycleReport` is emitted with code E002, listing [A, B, A] as the cycle

#### Scenario: Acyclic graph
- **GIVEN** a graph with no circular references
- **WHEN** the graph is built
- **THEN** no `CycleReport` entries are produced

### Requirement DG-06: AffectedSet and TieredTestSet Schemas

The system SHALL define the `AffectedSet` schema with fields: `changedFiles` (string[]), `directlyAffected` (ProjectDescriptor[]), `transitivelyAffected` (ProjectDescriptor[]), and `affectedTests` (TieredTestSet). The system SHALL define the `TieredTestSet` schema with fields: `unit` (ProjectDescriptor[]), `package` (ProjectDescriptor[]), `integration` (ProjectDescriptor[]), and `compatibility` (ProjectDescriptor[]).

#### Scenario: AffectedSet populated from changed files
- **GIVEN** a set of changed source files spanning two projects
- **WHEN** `titi affected` computes the affected set
- **THEN** the returned `AffectedSet` has `changedFiles` listing every modified file path, `directlyAffected` listing the two projects whose source changed, `transitivelyAffected` listing all downstream dependents, and `affectedTests` partitioned into the appropriate `TieredTestSet` tiers

#### Scenario: TieredTestSet populated by tier
- **GIVEN** the affected set includes one unit test project and one integration test project
- **WHEN** the `TieredTestSet` is constructed
- **THEN** the unit test project appears in `TieredTestSet.unit` and the integration test project appears in `TieredTestSet.integration`, with `TieredTestSet.package` and `TieredTestSet.compatibility` empty

### Requirement DG-07: Graph Fingerprinting

The system SHALL record a `FileFingerprint` (path, lastModified, sizeBytes, optional SHA-256 contentHash) for each .csproj and for global build files, storing them in `MonorepoGraph.fingerprints`.

#### Scenario: Fingerprint capture
- **WHEN** the graph is built
- **THEN** each .csproj under repoRoot has a corresponding fingerprint entry with at minimum path and lastModified populated

### Requirement DG-08: Graph Performance

The system SHALL meet the following performance targets on commodity hardware (4-core CPU, 16 GB RAM, SSD):
- Graph construction from scratch SHALL complete within 30 seconds for a monorepo containing up to 1000 .csproj files.
- Affected-set computation from a warm graph SHALL complete within 2 seconds for a monorepo containing up to 1000 .csproj files.
- Topological sort SHALL complete within 1 second for graphs up to 1000 nodes.

> **Benchmark methodology:** Performance tests SHALL use a synthetic monorepo fixture with the specified project count and a representative dependency density (average 5 dependencies per project). Each threshold is validated as the median of 5 consecutive runs on the specified hardware class. CI runners SHALL document their hardware tier to enable threshold adjustment via a scaling factor.

#### Scenario: Large repo graph construction
- **GIVEN** a monorepo with 800 .csproj files and typical inter-project dependency density
- **WHEN** the graph is constructed from scratch (cold cache)
- **THEN** construction completes in under 30 seconds

#### Scenario: Affected-set on warm graph
- **GIVEN** a warm graph cache for a 500-project monorepo and 10 changed files
- **WHEN** `titi affected` is run
- **THEN** the affected set is computed and printed in under 2 seconds

### Requirement DG-09: Single-Writer Concurrency

The system SHALL assume single-writer access to the `.titi/` directory. The system SHALL use a lock file (`.titi/graph.cache.lock`) to coordinate single-writer access. If a titi command detects that another titi process holds the lock, it SHALL wait up to 10 seconds for the lock to release, then emit a warn-level diagnostic and proceed with a fresh in-memory graph build rather than reading a partially written cache.

The lock protocol SHALL follow this state machine:

1. **UNLOCKED** → writer creates `.titi/graph.cache.lock` containing its PID and timestamp → **ACQUIRED**
2. **ACQUIRED** → writer begins writing `.titi/graph.cache.tmp` → **WRITING**
3. **WRITING** → writer completes the tmp file and atomically renames it to `.titi/graph.cache` → **RENAMED**
4. **RENAMED** → writer deletes `.titi/graph.cache.lock` → **UNLOCKED**

Crash recovery by state:
- Crash during **ACQUIRED**: lock file exists, no `.tmp` file. Next process detects stale lock via PID liveness check and removes it.
- Crash during **WRITING**: lock file and partial `.tmp` file exist. Next process detects stale lock, removes both the lock and the orphaned `.tmp` file, and proceeds. The previous `.titi/graph.cache` (if any) remains intact.
- Crash during **RENAMED**: `.titi/graph.cache` is valid (rename completed). Lock file is orphaned. Next process detects stale lock and removes it.

Stale lock detection SHALL use OS-level PID liveness checks (e.g. `kill(pid, 0)` on POSIX, `OpenProcess` on Windows). If the recorded PID is not running, the lock is considered stale. PID reuse is mitigated by also checking the lock file's timestamp: a lock older than 60 seconds with a non-running PID is unconditionally stale.

#### Scenario: Concurrent titi invocation
- **GIVEN** one `titi cache warm` process is writing to `.titi/graph.cache`
- **WHEN** a second `titi affected` process starts and detects the lock
- **THEN** the second process waits up to 10 seconds; if the lock is released, it reads the cache normally; if not, it emits a warning and builds the graph from scratch

#### Scenario: Stale lock file
- **GIVEN** a `.titi/graph.cache.lock` file exists but the owning process is no longer running
- **WHEN** a titi command starts
- **THEN** the system detects the stale lock (e.g. via PID check), removes it, and proceeds normally with a diagnostic note
