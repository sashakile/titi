# Graph Cache

## Purpose

The graph cache capability persists the computed `MonorepoGraph` to `.titi/graph.cache` and implements a tiered invalidation strategy to avoid unnecessary full graph rebuilds.

## Requirements

### Requirement: Cache Persistence

The system SHALL serialise the `GraphCache` (containing `schemaVersion`, the full `MonorepoGraph`, `fingerprints`, and `titiVersion`) to `.titi/graph.cache` after every successful graph construction.

#### Scenario: Cache written after build
- **WHEN** the graph is built successfully
- **THEN** `.titi/graph.cache` exists and contains a valid `GraphCache` document

#### Scenario: Cache not written on build failure
- **WHEN** graph construction fails (e.g., cycle detected aborting build)
- **THEN** any existing `.titi/graph.cache` is left unchanged

### Requirement: Cache Loading

The system SHALL attempt to load `GraphCache` from `.titi/graph.cache` at the start of any command that requires the graph, and use the cached graph when it is valid and not stale.

#### Scenario: Valid cache used
- **GIVEN** a valid, non-stale `.titi/graph.cache`
- **WHEN** `titi affected` is invoked
- **THEN** the graph is loaded from cache without re-scanning .csproj files

#### Scenario: Corrupt cache triggers full rebuild
- **GIVEN** `.titi/graph.cache` contains malformed content
- **WHEN** any command loads the graph
- **THEN** E006 (CACHE_CORRUPT) is emitted as a warning, the cache is deleted, and the graph is rebuilt from scratch

### Requirement: Subgraph Invalidation

The system SHALL perform a partial (subgraph) re-evaluation when one or more .csproj files have changed fingerprints, updating only the affected nodes without discarding the entire cached graph.

#### Scenario: Single .csproj modified
- **GIVEN** project X.csproj has a changed `lastModified` timestamp relative to its cached fingerprint
- **WHEN** the cache is loaded
- **THEN** only the subgraph rooted at X is re-evaluated; all other nodes are taken from the cache

#### Scenario: Multiple independent .csproj files modified
- **GIVEN** two unrelated projects A.csproj and B.csproj have changed fingerprints
- **WHEN** the cache is loaded
- **THEN** both subgraphs are re-evaluated independently and the cache is updated

### Requirement: Full Cache Invalidation

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

### Requirement: Schema Version Compatibility

The system SHALL reject a cached graph whose `schemaVersion` does not match the current schema version expected by the running titi binary, treating it as a full invalidation.

#### Scenario: Schema version mismatch
- **GIVEN** the cache file has `schemaVersion = "1"` but the current titi binary expects `"2"`
- **WHEN** the cache is loaded
- **THEN** E006 is emitted and the graph is rebuilt from scratch

### Requirement: Cache Warm Command

The system SHALL expose `titi cache warm` to explicitly pre-build and persist the graph cache, allowing CI pipelines to warm the cache before running other commands.

#### Scenario: Cache warmed in CI
- **GIVEN** a cold (no `.titi/graph.cache`) environment
- **WHEN** `titi cache warm` is invoked
- **THEN** `.titi/graph.cache` is written and the command exits 0

#### Scenario: Cache already warm
- **GIVEN** a valid, non-stale cache already exists
- **WHEN** `titi cache warm` is invoked
- **THEN** the cache is refreshed (fingerprints updated) and the command exits 0
