# Reference Swap

## Purpose

The reference swap capability replaces NuGet binary package references with local project source references (and vice versa) within a transient solution context, respecting version policy, TFM compatibility, and cycle-prevention rules.

## Requirements

### Requirement: Swap Request Processing

The system SHALL accept a `SwapRequest` specifying target projects, a `versionPolicy` (STRICT, SEMVER_COMPATIBLE, or FORCE), an `includeTransitive` flag, and a `force` override, then produce a `SwapResult` describing every reference decision.

#### Scenario: Successful source swap
- **GIVEN** a project references `Orion.Payments` as a NuGet package and a local source project for `Orion.Payments` exists under `sourceRoot`
- **WHEN** a swap with versionPolicy=STRICT is requested
- **THEN** the `PackageReference` is retained with `ExcludeAssets="All"` (so it remains in the NuGet graph for transitive resolution) AND a `ProjectReference` to the local source project is injected alongside it; the swapped entry appears in `SwapResult.swapped`

#### Scenario: Version mismatch retained
- **GIVEN** the local source project version does not match the requested version range and versionPolicy=STRICT
- **WHEN** the swap is attempted
- **THEN** the reference is retained with `RetainedReason.VERSION_MISMATCH` in `SwapResult.retained`

#### Scenario: TFM incompatible retained
- **GIVEN** the local source project targets `net8.0` but the consuming project requires `net9.0` only
- **WHEN** the swap is attempted
- **THEN** the reference is retained with `RetainedReason.TFM_INCOMPATIBLE`

#### Scenario: No local source retained
- **GIVEN** a NuGet package has no matching project under `sourceRoot`
- **WHEN** the swap is attempted
- **THEN** the reference is retained with `RetainedReason.NO_LOCAL_SOURCE`

### Requirement: Transitive Swap

The system SHALL recursively swap transitive package dependencies when `SwapRequest.includeTransitive` is true, following the topological order of the dependency graph.

#### Scenario: Transitive swap enabled
- **GIVEN** project A depends on B (NuGet) which depends on C (NuGet), and local sources exist for both B and C
- **WHEN** a swap with includeTransitive=true is requested for A
- **THEN** both B and C are swapped to source references in SwapResult.swapped

#### Scenario: Transitive swap disabled
- **GIVEN** the same setup as above
- **WHEN** includeTransitive=false
- **THEN** only B is swapped; C remains as a binary reference

### Requirement: Cycle Prevention

The system SHALL refuse any swap that would introduce a cycle into the project reference graph, retaining the offending reference with `RetainedReason.CYCLE_PREVENTION` and emitting a `CycleReport`.

#### Scenario: Cycle-creating swap refused
- **GIVEN** swapping X → Y (source) would cause Y to transitively depend on X
- **WHEN** the swap is attempted without force=true
- **THEN** X is retained with CYCLE_PREVENTION and a CycleReport is added to SwapResult.cycles

#### Scenario: Force override
- **GIVEN** the same cycle risk as above
- **WHEN** force=true is set on the SwapRequest
- **THEN** the swap proceeds and the caller is warned via a diagnostic event

#### Scenario: Partial transitive swap with mid-chain cycle
- **GIVEN** includeTransitive=true and swapping B (a direct dependency) is safe, but swapping C (a transitive dependency of B) would introduce a cycle
- **WHEN** the swap is attempted
- **THEN** B is swapped to a source reference, C is retained with `RetainedReason.CYCLE_PREVENTION`, and the `SwapResult` has both `swapped` (containing B) and `retained` (containing C) entries populated

### Requirement: MSBuild Context Injection

The system SHALL populate a `MSBuildContext` in `SwapResult` with `inTitiContext=true`, the configured prefix, sourceRoot, and any additional props required for the transient solution build to succeed.

#### Scenario: Context populated after swap
- **WHEN** at least one reference is swapped successfully
- **THEN** SwapResult.msbuildContext has inTitiContext=true, titiPrefix matching config.prefix, and titiSourceRoot matching config.sourceRoot

#### Scenario: Context when no swaps occur
- **WHEN** all references are retained (no swaps)
- **THEN** SwapResult.msbuildContext has inTitiContext=false

### Requirement: SEMVER_COMPATIBLE Policy

The system SHALL accept a local source project as a valid swap target when `versionPolicy=SEMVER_COMPATIBLE` if the local version's major matches and local version >= required minimum.

#### Scenario: Compatible minor bump accepted
- **GIVEN** package requires `>= 2.1.0` and local source is `2.3.0`
- **WHEN** versionPolicy=SEMVER_COMPATIBLE
- **THEN** the swap succeeds

#### Scenario: Major mismatch rejected under SEMVER_COMPATIBLE
- **GIVEN** package requires `>= 2.0.0` and local source is `3.0.0`
- **WHEN** versionPolicy=SEMVER_COMPATIBLE
- **THEN** the reference is retained with VERSION_MISMATCH
