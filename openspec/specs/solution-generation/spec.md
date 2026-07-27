# Solution Generation

## Purpose

The solution generation capability creates transient .slnx solution files scoped to a target project and its dependency closure, stored under the configured cache directory and excluded from version control.

> **Path convention:** Paths follow `$(cache.directory)` (see `configuration` spec, CF-03); defaults are shown as `.titi/` for readability.

## Requirements

### Requirement SG-01: Transient Solution File Creation

The system SHALL generate a `.slnx` solution file using the `Microsoft.VisualStudio.SolutionPersistence` library, writing the output to `.titi/solutions/<name>.slnx`, and the file MUST be listed in `.gitignore`.

#### Scenario: Solution created
- **GIVEN** a target project with a known dependency closure
- **WHEN** `titi open <project>` is invoked
- **THEN** a `.slnx` file exists under `.titi/solutions/` containing entries for the target project and all its swapped dependencies

#### Scenario: Solutions directory gitignored
- **GIVEN** titi has been initialised in a repo
- **WHEN** the `.titi/` directory is inspected
- **THEN** `.gitignore` (or the repo root `.gitignore`) contains an entry excluding `.titi/`

### Requirement SG-02: Solution Spec Model

The system SHALL represent each solution to generate as a `SolutionSpec` with `format` (`"slnx"` | `"sln"`), `outputPath`, a list of `SolutionProjectEntry` items, virtual `folders`, and `globalProperties`.

#### Scenario: SLNX format default
- **WHEN** no format override is provided
- **THEN** the generated file uses the `.slnx` XML format

#### Scenario: Legacy SLN format
- **GIVEN** `solutionSpec.format = "sln"`
- **WHEN** the solution is written
- **THEN** the output file uses the legacy `.sln` text format

> **Note:** `Microsoft.VisualStudio.SolutionPersistence` supports both `.sln` and `.slnx` serialization. Legacy `.sln` support is lower priority than `.slnx` support.

> **Type constraint:** For `.sln` format, each `SolutionProjectEntry` SHALL include a `projectTypeGuid` (a Visual Studio project-type GUID). For `.slnx` format, `projectTypeGuid` is not required. Implementations SHOULD use format-specific entry types (e.g. `SlnProjectEntry` with required GUID vs `SlnxProjectEntry` without) to enforce this structurally.

### Requirement SG-03: Project Entry Metadata

The system SHALL populate each `SolutionProjectEntry` with `path`, a deterministic `projectGuid`, `displayName`, and an optional `folderPath` for solution folder organisation.

#### Scenario: Display name derived from project
- **WHEN** a project entry is added to the solution
- **THEN** `displayName` matches the project's assembly name or file name without extension

#### Scenario: Deterministic GUID
- **GIVEN** the same project path
- **WHEN** the solution is regenerated
- **THEN** the `projectGuid` for that entry is identical across regenerations

### Requirement SG-04: Global Properties Injection

The system SHALL write `globalProperties` from `SolutionSpec` into the generated solution, sourced from `SwapResult.msbuildContext` (see `reference-swap` spec, RS-04). When at least one swap occurred, the solution SHALL include `InTitiContext=true`, `TitiPrefix`, and `TitiSourceRoot`. When no swaps occurred, `InTitiContext` SHALL be `false`, the solution SHALL include the target project and all its dependency closure projects, but SHALL NOT inject swap-activated MSBuild targets (Directory.Build.targets remain inactive because InTitiContext=false).

#### Scenario: Properties present when swaps occur
- **WHEN** a transient solution is written after at least one reference was swapped
- **THEN** the solution file includes `InTitiContext=true`, `TitiPrefix`, and `TitiSourceRoot` at the solution global-properties level

#### Scenario: No swap properties when all references retained
- **WHEN** a transient solution is written and no references were swapped (all retained)
- **THEN** the solution file includes `InTitiContext=false` at the solution global-properties level; the full dependency closure is still included as binary-referenced projects

### Requirement SG-05: Idempotent Regeneration

The system SHALL regenerate an existing transient solution file if the dependency closure or swap state has changed since the file was last written, as determined by comparing the current set of `SolutionProjectEntry` paths and a serialized swap fingerprint against those recorded for the prior generation. titi SHALL persist this comparison metadata in a sidecar file at `.titi/solutions/<name>.fingerprint.edn` containing at minimum the solution project-path set, the `SwapResult` fingerprint, and the `globalProperties` fingerprint used to generate the solution. The system SHALL leave the `.slnx` file unchanged if the comparison produces no differences.

> **Swap fingerprint definition:** The `SwapResult` fingerprint SHALL be the SHA-256 digest over the sorted set of `SwapResult.swapped` entry IDs concatenated with a canonical serialisation of `SwapResult.msbuildContext` (sorted by property name). The `globalProperties` fingerprint SHALL be the SHA-256 digest over the same `globalProperties` map written into the solution. Both fingerprints SHALL be stable across regenerations given identical inputs (no timestamps, no unordered-map iteration order).

#### Scenario: Regeneration on change
- **GIVEN** a transient solution already exists for project P
- **WHEN** a new dependency is added to P and `titi open P` is run again
- **THEN** the solution file is rewritten to include the new dependency

#### Scenario: No regeneration when unchanged
- **GIVEN** a transient solution exists, its `.titi/solutions/P.fingerprint.edn` sidecar is present, and no dependencies have changed
- **WHEN** `titi open P` is run again
- **THEN** the solution file's modification timestamp is not updated

### Requirement SG-06: Solution Cleanup

The system SHALL remove all files under `.titi/solutions/` when `titi clean` is invoked.

#### Scenario: Clean removes solutions
- **GIVEN** multiple transient .slnx files exist under `.titi/solutions/`
- **WHEN** `titi clean` is invoked
- **THEN** all files under `.titi/solutions/` are deleted
