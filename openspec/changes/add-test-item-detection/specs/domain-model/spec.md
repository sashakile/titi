## ADDED Requirements

### Requirement: Test Item (DM-07)

The system SHALL represent each individual test method as a `TestItem` containing: `testId` (unique string combining assembly, class, and method), `assemblyPath` (path to the test .dll), `className` (fully qualified class name including namespace), `methodName` (test method name, with serialized arguments for parameterized tests), `framework` (:xunit | :nunit | :mstest), `tier` (:unit | :integration | :compatibility), `sourceFile` (test source file path, if available), `lastOutcome` (:passed | :failed | :skipped | nil), `meanDurationMs` (float, nil if unknown), and `tags` (vector of keyword).

> **Test ID format:** `testId` SHALL use `::` as the separator between assembly, class, and method components: `<assembly>::<namespace.class>::<method[(args)]>`. The `::` separator is chosen because it cannot appear in C# identifiers, namespace names, or common test arguments. For parameterized tests, method arguments SHALL be serialized using the same format as VSTest's `FullyQualifiedName` property, with special characters percent-encoded.

> **Framework detection:** The system SHALL detect the test framework from VSTest `--list-tests` output markers — xUnit (`[Fact]`, `[Theory]`), NUnit (`[Test]`, `[TestCase]`), and MSTest (`[TestMethod]`). Unrecognised frameworks SHALL be classified as `:unknown` and still enumerated.

#### Scenario: xUnit Fact test
- **WHEN** `dotnet test --list-tests` enumerates `Orion.Core.Tests` and reports test method `ParseValidInput` in class `ParserTests` in namespace `Orion.Core.Tests`
- **THEN** a `TestItem` is created with testId `"Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseValidInput"`, assemblyPath `"Orion.Core.Tests/bin/Debug/net10.0/Orion.Core.Tests.dll"`, className `"Orion.Core.Tests.ParserTests"`, methodName `"ParseValidInput"`, framework `:xunit`, lastOutcome nil, meanDurationMs nil

#### Scenario: NUnit parameterized test case
- **WHEN** `dotnet test --list-tests` enumerates test case `InsertRow(42)` in class `TableTests` in namespace `Orion.Data.Tests`
- **THEN** a `TestItem` is created with testId `"Orion.Data.Tests::Orion.Data.Tests.TableTests::InsertRow(42)"`, className `"Orion.Data.Tests.TableTests"`, methodName `"InsertRow(42)"`, framework `:nunit`

#### Scenario: Nested class test
- **WHEN** `dotnet test --list-tests` enumerates a test in a nested class `ParserTests+NestedTests` (C# nested class syntax uses `+`)
- **THEN** the `className` field preserves the `+` separator: `"Orion.Core.Tests.ParserTests+NestedTests"`, and `testId` uses this preserved name

#### Scenario: Generic method with type parameters
- **WHEN** `dotnet test --list-tests` enumerates a generic test method `CreateInstance<Foo>` in class `FactoryTests`
- **THEN** methodName is `"CreateInstance<Foo>"` (preserving angle brackets) and testId uses this methodName unchanged

#### Scenario: Test without prior outcome or duration
- **WHEN** a TestItem is created after first discovery with no prior run history
- **THEN** `lastOutcome` is nil, `meanDurationMs` is nil, and the test is added to the always-run set

### Requirement: Test-to-Source Edge (DM-08)

The system SHALL represent a dependency edge between a test and a source file as a `TestToSourceEdge` containing: `testId` (matching a `TestItem.testId`), `sourceFile` (absolute path to the source file), `origin` (:static | :runtime | :manual), `weight` (integer confidence in parts per million, default 1_000_000 for multiplicative identity), and `lineRanges` (vector of `[start, end]` integer tuples, optional).

> **Origin conventions:** Edges derived from coverage data (Cobertura XML from `dotnet test --collect "XPlat Code Coverage"`) SHALL have origin `:static`. Edges derived from post-execution coverage feedback (the `ingest` pipeline's runtime edge recording) SHALL have origin `:runtime`. Manually configured edges SHALL have origin `:manual`. This follows the testaruda convention (TIA-ADAPT-009), where "static" means determined by coverage analysis and "runtime" means fed back after test execution.
>
> **Invariant — Weight range:** `weight` SHALL be in the range `[0, 1_000_000]` where `1_000_000` is the multiplicative identity (full confidence) and `0` is the additive identity (no evidence).

#### Scenario: Static edge from coverage
- **GIVEN** a Cobertura coverage report shows test `ParseValidInput` exercises lines 42–58 of `src/Orion.Core/Parser.cs`
- **WHEN** the coverage report is ingested
- **THEN** a `TestToSourceEdge` is created with testId `"Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseValidInput"`, sourceFile `"src/Orion.Core/Parser.cs"`, origin `:static`, weight `1_000_000`, and lineRanges `[[42, 58]]`

#### Scenario: Manual edge
- **WHEN** a manual override adds an edge from a test to a source file
- **THEN** a `TestToSourceEdge` is created with origin `:manual` and configurable weight

### Requirement: Selection Result (DM-09)

The system SHALL represent the result of selecting a test item as a `TestSelectionResult` containing: `testId`, `selected` (boolean), `reasons` (vector of `{:kind reason-kind, :description string}`), `confidence` (float in [0, 1]), and `fallbackReason` (string or nil).

#### Scenario: Test selected via direct change
- **WHEN** a source file that a test depends on is in the change set
- **THEN** the selection result has `selected: true` and reasons containing a `:direct-change` entry with the file path

#### Scenario: Test selected via always-run set
- **WHEN** a test failed in its last run
- **THEN** the selection result has `selected: true` and reasons containing an `:always-run` entry with reason `:last-run-failed`

### Requirement: Always-Run and Fallback Reason Enums (DM-10)

The system SHALL define AlwaysRunReason with values `:last-run-failed`, `:newly-added`, `:no-history`, `:must-run`, `:quarantined` and FallbackReason with values `:confidence-below-threshold`, `:unresolved-file`, `:adapter-failure`, `:environment-change`.

> **"Newly added" semantics:** A test is considered `:newly-added` only if it was not present in the most recent recorded run history (from `titi tests record` or `titi tests ingest`). Tests discovered by `titi tests list` alone (without a subsequent recording) are not "newly added" — they are treated as `:no-history`. This prevents always-run-set inflation when discovery is re-run without a corresponding recording.

#### Scenario: Always-run enum values
- **WHEN** any of the AlwaysRunReason values is used to describe why a test is unconditionally selected
- **THEN** it SHALL be a keyword matching one of the accepted enum values

#### Scenario: Fallback enum values
- **WHEN** any of the FallbackReason values is used to describe a full-suite fallback trigger
- **THEN** it SHALL be a keyword matching one of the accepted enum values

### Requirement: Missed-Selection Incident (DM-11)

The system SHALL represent a missed-selection incident as a `MissedSelectionIncident` containing: `changedContent` (file path string), `missedTestId` (string), `timestamp` (ISO 8601), and `status` (:candidate | :promoted | :dismissed).

#### Scenario: Incident recorded
- **WHEN** a full run reveals a test failure that the most recent selection would have skipped
- **THEN** a new `MissedSelectionIncident` is created with status `:candidate`

#### Scenario: Incident promoted
- **GIVEN** the same `(changedContent, missedTestId)` pair has been observed as `:candidate` at least 3 times
- **WHEN** incidents are processed
- **THEN** the most recent incident's status is promoted to `:promoted`
