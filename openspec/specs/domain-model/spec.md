# Domain Model

## Purpose

The titi domain model defines the core data types used throughout the tool for describing projects, packages, versions, and inter-project relationships.

## Terminology

The following terms have precise meanings throughout all titi specifications:

- **project**: a local `.csproj` file in the monorepo, represented by a `ProjectDescriptor`
- **package**: a NuGet binary artifact resolved from a feed, referenced via `PackageReference`
- **packable project**: a local project with `isPackable=true` that produces a NuGet package when packed
- **package ID**: the NuGet identity string (e.g. `Orion.Core`) — shared between a packable project and the package it produces
- **source reference**: a `ProjectReference` pointing to a local project (see `ReferenceMode.SOURCE`)
- **binary reference**: a `PackageReference` resolved from a NuGet feed (see `ReferenceMode.BINARY`)

## Requirements

### Requirement DM-01: Project Descriptor

The system SHALL represent each .NET project as a `ProjectDescriptor` containing its path, package identity, version, target frameworks, packability flag, test project flag, package references, project references, and arbitrary MSBuild properties.

#### Scenario: Full project descriptor
- **GIVEN** a .csproj file with NuGet metadata, TFM list, and package/project references
- **WHEN** the project is parsed into a descriptor
- **THEN** the descriptor contains path, packageId, a `SemanticVersion`, a non-empty `TFM[]`, `isPackable`, `isTestProject`, `PackageRef[]`, `ProjectRef[]`, and a properties map

#### Scenario: Minimal project descriptor
- **GIVEN** a .csproj with no NuGet metadata and a single TFM
- **WHEN** the project is parsed
- **THEN** packageId and version are absent (not empty strings), and `isPackable` is false

> **Invariant:** `isPackable=true` implies both `packageId` and `version` are present. Absent fields SHALL be represented as the language's native absence value (e.g. `nil`, `None`, `null`), never as empty strings.

### Requirement DM-02: Semantic Version

The system SHALL represent version strings as a structured `SemanticVersion` with `major`, `minor`, `patch`, integer fields plus optional `prerelease` and `metadata` string components.

#### Scenario: Stable version parse
- **WHEN** the string `"3.2.1"` is parsed as a SemanticVersion
- **THEN** major=3, minor=2, patch=1, prerelease is absent, metadata is absent

#### Scenario: Prerelease version parse
- **WHEN** the string `"1.0.0-beta.4+build.99"` is parsed
- **THEN** major=1, minor=0, patch=0, prerelease="beta.4", metadata="build.99"

#### Scenario: Invalid version string
- **WHEN** a non-SemVer string such as `"latest"` is parsed
- **THEN** the system raises a structured error with code E009

### Requirement DM-03: Target Framework Moniker

The system SHALL represent each target framework as a `TFM` constructed from a `moniker` string (e.g. `"net9.0"`). The `framework` identifier and `version` component SHALL be derived (computed) from the moniker at construction time and MUST NOT be independently settable.

#### Scenario: Standard TFM parse
- **WHEN** the moniker `"net9.0"` is parsed
- **THEN** framework is `"net"` and version is `"9.0"`

#### Scenario: Cross-target project
- **GIVEN** a project targeting `net8.0` and `net9.0`
- **WHEN** its TFMs are collected
- **THEN** the descriptor holds two `TFM` entries

### Requirement DM-04: Package Reference

The system SHALL represent each NuGet package reference as a `PackageRef` with `packageId`, a parsed `NuGetVersionRange`, and optional `privateAssets` and `excludeAssets` attributes. The `NuGetVersionRange` SHALL be a self-validating value object that rejects invalid NuGet version range syntax at construction time and exposes semantic operations (e.g. `satisfiedBy(version)`, `floor()`, `isExactPin()`). Version range syntax and satisfaction semantics are defined by the [NuGet Package Versioning specification](https://learn.microsoft.com/nuget/concepts/package-versioning#version-ranges) which is normative for this type.

#### Scenario: Standard package reference
- **WHEN** `<PackageReference Include="Newtonsoft.Json" Version="13.0.1" />` is parsed
- **THEN** packageId is `"Newtonsoft.Json"` and versionRange is a `NuGetVersionRange` representing `>= 13.0.1`

#### Scenario: Reference with private assets
- **WHEN** a `PackageReference` with `PrivateAssets="All"` is parsed
- **THEN** privateAssets is `"All"` on the resulting `PackageRef`

### Requirement DM-05: Project Reference

The system SHALL represent each project-to-project dependency as a `ProjectRef` with the referenced project's `path` and an `isTransitive` flag.

#### Scenario: Direct project reference
- **WHEN** `<ProjectReference Include="../Foo/Foo.csproj" />` is parsed
- **THEN** path resolves to the canonical absolute path of Foo.csproj and isTransitive is false

#### Scenario: Transitive project reference
- **WHEN** a project reference is marked as transitive by the graph engine
- **THEN** isTransitive is true on the resulting `ProjectRef`

### Requirement DM-06: Reference Mode

The system SHALL define a `ReferenceMode` enumeration with values `SOURCE`, `BINARY`, and `SUPPRESSED` to indicate how a dependency edge is currently resolved.

#### Scenario: Source mode
- **WHEN** a package dependency is satisfied by a local project source
- **THEN** the edge carries `ReferenceMode.SOURCE`

#### Scenario: Binary mode
- **WHEN** a package dependency is satisfied by a NuGet binary
- **THEN** the edge carries `ReferenceMode.BINARY`

#### Scenario: Suppressed mode
- **WHEN** a dependency is intentionally excluded from resolution
- **THEN** the edge carries `ReferenceMode.SUPPRESSED`
