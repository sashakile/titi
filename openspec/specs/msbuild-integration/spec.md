# MSBuild Integration

## Purpose

The MSBuild integration capability defines the property and target contract between titi and the MSBuild evaluation pipeline, including `Directory.Build.props` additions, the swap-logic targets, and the set of properties that form a breaking public contract.

> **Architecture Note — Relationship to Reference Swap:** The `Directory.Build.targets` swap logic (MB-02) is the **execution** layer — it performs convention-based prefix matching at MSBuild evaluation time. It does NOT enforce version policy, TFM compatibility, or cycle-prevention rules. Those safety checks are performed by the reference-swap engine (spec `reference-swap`) at planning time, before the transient solution is generated. The targets intentionally use a simple, deterministic rule (prefix match + source exists) so that MSBuild evaluation remains fast and predictable. If a developer opens a titi-generated `.slnx`, only projects that passed reference-swap validation are included in the solution's dependency closure, so the targets will only encounter pre-validated swap candidates under normal usage.

## Requirements

### Requirement MB-01: Directory.Build.props Additions

The system SHALL add four MSBuild properties to `Directory.Build.props`: `TitiPrefix`, `TitiSourceRoot`, `InTitiContext` (defaulting to `false`), and `AccelerateBuildsInVisualStudio`.

#### Scenario: Properties present after init
- **GIVEN** `titi init` has been run in a repo
- **WHEN** `Directory.Build.props` is inspected
- **THEN** it contains definitions for `TitiPrefix`, `TitiSourceRoot`, `InTitiContext`, and `AccelerateBuildsInVisualStudio`

#### Scenario: InTitiContext default is false
- **WHEN** a normal build is performed outside of a titi-generated solution
- **THEN** `InTitiContext` evaluates to `false` and no package references are swapped

### Requirement MB-02: Swap Logic Targets

The system SHALL implement `Directory.Build.targets` swap logic that, when `InTitiContext=true`, discovers each `PackageReference` whose ID matches `$(TitiPrefix)*` and has a corresponding project under `$(TitiSourceRoot)`, sets `ExcludeAssets=All` on the package reference, and injects a `ProjectReference` to the local source project.

#### Scenario: Package swapped via targets
- **GIVEN** `InTitiContext=true` and `TitiPrefix=Orion.` and a `PackageReference` to `Orion.Payments`
- **WHEN** `Directory.Build.targets` is evaluated
- **THEN** `Orion.Payments` has `ExcludeAssets=All` and a `ProjectReference` to `$(TitiSourceRoot)/Orion.Payments/Orion.Payments.csproj` is added

#### Scenario: Non-prefixed package untouched
- **GIVEN** `InTitiContext=true` and a `PackageReference` to `Newtonsoft.Json`
- **WHEN** `Directory.Build.targets` is evaluated
- **THEN** `Newtonsoft.Json` is unchanged

#### Scenario: Prefixed package without local source
- **GIVEN** `InTitiContext=true` and a `PackageReference` to `Orion.External` with no matching source under `TitiSourceRoot`
- **WHEN** `Directory.Build.targets` is evaluated
- **THEN** the package reference is left as-is (binary) and no ProjectReference is injected

> **Note — Manual InTitiContext bypass:** If a developer manually sets `InTitiContext=true` in a non-titi-generated solution, prefix-matched packages are swapped without reference-swap safety checks. This is unsupported: titi provides no guarantees about version compatibility, TFM alignment, or cycle safety in this scenario.

### Requirement MB-03: Breaking Property Contract

The system SHALL treat the three properties `InTitiContext`, `TitiPrefix`, and `TitiSourceRoot` as titi-owned public breaking contract properties: renaming or removing any of them constitutes a breaking change requiring a major version bump.

> **Note:** `AccelerateBuildsInVisualStudio` is NOT a titi-defined property. It is a Visual Studio convention that titi adopts by setting it in the generated solution. titi does not own its contract; changes to its semantics are governed by Visual Studio, not titi's versioning policy.

#### Scenario: Contract properties documented
- **WHEN** the titi changelog or release notes are inspected
- **THEN** any modification to the titi-owned contract properties (`InTitiContext`, `TitiPrefix`, `TitiSourceRoot`) is explicitly flagged as breaking

#### Scenario: Downstream build uses contract properties
- **GIVEN** a consumer's `Directory.Build.targets` references `$(InTitiContext)` by name
- **WHEN** a new version of titi is installed
- **THEN** the property name remains `InTitiContext` and the consumer's targets continue to evaluate correctly

### Requirement MB-04: AccelerateBuildsInVisualStudio

The system SHALL set `AccelerateBuildsInVisualStudio=true` in the titi-generated solution's global properties to enable Visual Studio's build acceleration feature. Note: this is a Visual Studio convention, **not** a titi-owned property — see Requirement "Breaking Property Contract" above for ownership boundaries.

#### Scenario: Acceleration enabled in titi solution
- **GIVEN** a transient .slnx generated by titi
- **WHEN** the solution is opened in Visual Studio
- **THEN** `AccelerateBuildsInVisualStudio` is `true` for all projects in the solution

#### Scenario: Acceleration not forced outside titi
- **GIVEN** a project is built outside of a titi-generated solution
- **WHEN** `AccelerateBuildsInVisualStudio` is evaluated
- **THEN** its value comes from the repo's own `Directory.Build.props` default, which MAY be `false`

### Requirement MB-05: MSBuild Locator Initialisation

The system SHALL call `Microsoft.Build.Locator.RegisterDefaults()` before referencing any MSBuild type. If no valid MSBuild installation is found, the system SHALL emit E007 (MSBUILD_NOT_FOUND) and abort.

#### Scenario: MSBuild located successfully
- **GIVEN** a valid .NET SDK installation is present on the system
- **WHEN** any MSBuild-dependent operation is initialised
- **THEN** `RegisterDefaults()` completes without error and MSBuild types are available for use

#### Scenario: No MSBuild installation found
- **GIVEN** no valid MSBuild installation can be located by the Locator
- **WHEN** any MSBuild-dependent operation is initialised
- **THEN** E007 (MSBUILD_NOT_FOUND) is emitted with an actionable suggestion and the command exits with code 1

### Requirement MB-06: MSBuild Not Found Handling

The system SHALL detect when `dotnet` is not available on PATH before invoking any MSBuild-dependent operation and emit error E007 (MSBUILD_NOT_FOUND) with an actionable suggestion.

#### Scenario: dotnet not on PATH
- **GIVEN** the `dotnet` executable is not found in any PATH entry
- **WHEN** a command requiring MSBuild evaluation is invoked
- **THEN** the system emits E007 with a suggestion to install the .NET SDK, and exits with code 1
