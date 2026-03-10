# Solution Generation

## Purpose

The solution generation capability creates transient .slnx solution files scoped to a target project and its dependency closure, stored under `.titi/solutions/` and excluded from version control.

## Requirements

### Requirement: Transient Solution File Creation

The system SHALL generate a `.slnx` solution file using the `Microsoft.VisualStudio.SolutionPersistence` library, writing the output to `.titi/solutions/<name>.slnx`, and the file MUST be listed in `.gitignore`.

#### Scenario: Solution created
- **GIVEN** a target project with a known dependency closure
- **WHEN** `titi open <project>` is invoked
- **THEN** a `.slnx` file exists under `.titi/solutions/` containing entries for the target project and all its swapped dependencies

#### Scenario: Solutions directory gitignored
- **GIVEN** titi has been initialised in a repo
- **WHEN** the `.titi/` directory is inspected
- **THEN** `.gitignore` (or the repo root `.gitignore`) contains an entry excluding `.titi/`

### Requirement: Solution Spec Model

The system SHALL represent each solution to generate as a `SolutionSpec` with `format` (`"slnx"` | `"sln"`), `outputPath`, a list of `SolutionProjectEntry` items, virtual `folders`, and `globalProperties`.

#### Scenario: SLNX format default
- **WHEN** no format override is provided
- **THEN** the generated file uses the `.slnx` XML format

#### Scenario: Legacy SLN format
- **GIVEN** `solutionSpec.format = "sln"`
- **WHEN** the solution is written
- **THEN** the output file uses the legacy `.sln` text format

### Requirement: Project Entry Metadata

The system SHALL populate each `SolutionProjectEntry` with `path`, a deterministic `projectGuid`, `displayName`, and an optional `folderPath` for solution folder organisation.

#### Scenario: Display name derived from project
- **WHEN** a project entry is added to the solution
- **THEN** `displayName` matches the project's assembly name or file name without extension

#### Scenario: Deterministic GUID
- **GIVEN** the same project path
- **WHEN** the solution is regenerated
- **THEN** the `projectGuid` for that entry is identical across regenerations

### Requirement: Global Properties Injection

The system SHALL write `globalProperties` from `SolutionSpec` into the generated solution so that `InTitiContext`, `TitiPrefix`, and `TitiSourceRoot` are set for all builds initiated from the transient solution.

#### Scenario: Properties present in generated solution
- **WHEN** a transient solution is written
- **THEN** the solution file includes `InTitiContext=true`, `TitiPrefix`, and `TitiSourceRoot` at the solution global-properties level

### Requirement: Idempotent Regeneration

The system SHALL regenerate an existing transient solution file if the dependency closure or swap state has changed since the file was last written, and leave it unchanged if nothing has changed.

#### Scenario: Regeneration on change
- **GIVEN** a transient solution already exists for project P
- **WHEN** a new dependency is added to P and `titi open P` is run again
- **THEN** the solution file is rewritten to include the new dependency

#### Scenario: No regeneration when unchanged
- **GIVEN** a transient solution exists and no dependencies have changed
- **WHEN** `titi open P` is run again
- **THEN** the solution file's modification timestamp is not updated

### Requirement: Solution Cleanup

The system SHALL remove all files under `.titi/solutions/` when `titi clean` is invoked.

#### Scenario: Clean removes solutions
- **GIVEN** multiple transient .slnx files exist under `.titi/solutions/`
- **WHEN** `titi clean` is invoked
- **THEN** all files under `.titi/solutions/` are deleted
