# Graph Cache

## Purpose

The graph cache capability persists the computed `MonorepoGraph` to `.titi/graph.cache` and implements a tiered invalidation strategy to avoid unnecessary full graph rebuilds.

## Requirements

### Requirement GC-01: Cache Persistence

The system SHALL serialise the `GraphCache` (containing `schemaVersion`, the full `MonorepoGraph`, `fingerprints`, and `titiVersion`) to `.titi/graph.cache` after every successful graph construction.

#### Scenario: Cache written after build
- **WHEN** the graph is built successfully
- **THEN** `.titi/graph.cache` exists and contains a valid `GraphCache` document

#### Scenario: Cache not written on build failure
- **WHEN** graph construction fails (e.g., cycle detected aborting build)
- **THEN** any existing `.titi/graph.cache` is left unchanged

### Requirement GC-02: Cache Loading

The system SHALL attempt to load `GraphCache` from `.titi/graph.cache` at the start of any command that requires the graph, and use the cached graph when it is valid and not stale.

> **Definition — Valid Cache:** A `GraphCache` is valid when ALL of the following hold: (1) the file is parseable (not corrupt), (2) `schemaVersion` matches the running binary's expected schema version (see GC-05), (3) `titiVersion` matches the running binary's version (see GC-04), (4) cache age does not exceed `cache.maxAge` (see GC-04), and (5) no global trigger file has a changed fingerprint (see GC-04). A cache that fails any condition is invalid and triggers the appropriate invalidation path (subgraph for condition violations addressable by GC-03, full rebuild for all others).

#### Scenario: Valid cache used
- **GIVEN** a valid, non-stale `.titi/graph.cache`
- **WHEN** `titi affected` is invoked
- **THEN** the graph is loaded from cache without re-scanning .csproj files

#### Scenario: Corrupt cache triggers full rebuild
- **GIVEN** `.titi/graph.cache` contains malformed content
- **WHEN** any command loads the graph
- **THEN** E006 (CACHE_CORRUPT) is emitted as a warning, the cache is deleted, and the graph is rebuilt from scratch

### Requirement GC-03: Subgraph Invalidation

The system SHALL perform a partial (subgraph) re-evaluation when one or more .csproj files have changed fingerprints, updating only the affected nodes without discarding the entire cached graph. The re-evaluated subgraph consists of the changed node X AND all nodes that transitively depend on X (the downstream dependent cone); X's upstream dependencies are not re-evaluated unless they are also directly changed.

> **Invariant — Invalidation Set:** For a set of changed nodes C, the invalidated set is defined as: `InvalidatedSet(C) = C ∪ { N ∈ V : ∃ x ∈ C such that x ∈ TransitiveDeps(N) }` — that is, C plus every node whose transitive dependency closure includes at least one member of C. All nodes NOT in `InvalidatedSet(C)` SHALL be taken from the cache unchanged.

#### Scenario: Single .csproj modified
- **GIVEN** project X.csproj has a changed `lastModified` timestamp relative to its cached fingerprint
- **WHEN** the cache is loaded
- **THEN** X and all nodes that transitively depend on X (the downstream dependent cone) are re-evaluated; upstream dependencies of X and unrelated nodes are taken from the cache unchanged

#### Scenario: Multiple independent .csproj files modified
- **GIVEN** two unrelated projects A.csproj and B.csproj have changed fingerprints
- **WHEN** the cache is loaded
- **THEN** both downstream dependent cones (A and its dependents; B and its dependents) are re-evaluated independently and the cache is updated

### Requirement GC-04: Full Cache Invalidation

The system SHALL discard the entire cached graph and perform a full rebuild when any global trigger file changes, when the titi tool version changes, or when the cache age exceeds `maxAge`.

#### Scenario: Global trigger file changed
- **GIVEN** `Directory.Build.props` has a different fingerprint than recorded in the cache
- **WHEN** the cache is loaded
- **THEN** the cache is fully discarded and the graph is rebuilt from scratch

#### Scenario: titi version changed
- **GIVEN** `GraphCache.titiVersion` does not match the currently running titi version
- **WHEN** the cache is loaded
- **THEN** the cache is fully invalidated and the graph is rebuilt

#### Scenario: Cache age exceeded
- **GIVEN** the cache was written more than `cache.maxAge` ago
- **WHEN** the cache is loaded
- **THEN** the cache is fully invalidated and the graph is rebuilt

### Requirement GC-05: Schema Version Compatibility

The system SHALL reject a cached graph whose `schemaVersion` does not match the current schema version expected by the running titi binary, treating it as a full invalidation. Unlike the config `schemaVersion` (see `configuration` spec, CF-02) which uses forward-compatible defaults for older schemas, the cache uses strict equality because cache format changes require a full rebuild to ensure data integrity.

#### Scenario: Schema version mismatch
- **GIVEN** the cache file has `schemaVersion = "1"` but the current titi binary expects `"2"`
- **WHEN** the cache is loaded
- **THEN** E006 is emitted and the graph is rebuilt from scratch

### Requirement GC-06: Cache Write Failure Resilience

The system SHALL degrade gracefully when the `.titi/` directory is not writable: a warn-level diagnostic is emitted and the command continues using the in-memory graph for the remainder of its execution without aborting.

#### Scenario: Cache directory not writable
- **GIVEN** the `.titi/` directory exists but the running process does not have write permission to it
- **WHEN** any command attempts to write the graph cache
- **THEN** the system emits a warn-level diagnostic indicating the cache could not be written, continues execution with the in-memory graph, and exits with the appropriate code for the command's logical outcome (not code 1 solely due to the write failure)

### Requirement GC-07: Cache Warm Command

The system SHALL expose `titi cache warm` to explicitly pre-build and persist the graph cache, allowing CI pipelines to warm the cache before running other commands.

#### Scenario: Cache warmed in CI
- **GIVEN** a cold (no `.titi/graph.cache`) environment
- **WHEN** `titi cache warm` is invoked
- **THEN** `.titi/graph.cache` is written and the command exits 0

#### Scenario: Cache already warm
- **GIVEN** a valid, non-stale cache already exists
- **WHEN** `titi cache warm` is invoked
- **THEN** the cache is refreshed (fingerprints updated) and the command exits 0

### Requirement GC-08: Atomic Cache Writes

The system SHALL write the graph cache atomically by first writing to a temporary file (`.titi/graph.cache.tmp`) and then renaming it to `.titi/graph.cache` only after the write completes successfully. If the process is interrupted during the write, the previous cache file (if any) SHALL remain intact and usable.

#### Scenario: Interrupted write preserves previous cache
- **GIVEN** a valid `.titi/graph.cache` exists from a prior build
- **WHEN** a cache write is interrupted (e.g. process killed) while writing `.titi/graph.cache.tmp`
- **THEN** `.titi/graph.cache` still contains the previous valid cache; `.titi/graph.cache.tmp` is an orphan that is cleaned up on next successful cache write

#### Scenario: Successful atomic rename
- **WHEN** a cache write completes and `.titi/graph.cache.tmp` is fully written
- **THEN** the file is atomically renamed to `.titi/graph.cache` and no intermediate state is observable by concurrent readers

> **Concurrency Note:** Atomicity relies on the OS `rename(2)` (POSIX) or `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING` (Windows) guarantee that the target path is updated in a single directory operation. Readers are lock-free: they open `.titi/graph.cache` without acquiring a lock. A reader that opens the file before the rename sees the previous contents; a reader that opens after sees the new contents. No reader observes a partially written file because the `.tmp` path is never visible as `.titi/graph.cache`. This model assumes a local filesystem (not NFS or network shares) where rename atomicity holds.
