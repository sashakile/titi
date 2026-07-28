## MODIFIED Requirements

### Requirement DX-02: Error Code Taxonomy

The system SHALL define and document the following error codes with their severity classification. **Fatal** errors abort the current command immediately; **aggregatable** errors are collected and reported together at the end of the current phase.

| Code | Name | Severity |
|------|------|----------|
| E001 | GRAPH_BUILD_FAILED | Fatal |
| E002 | CYCLE_DETECTED | Fatal |
| E003 | VERSION_MISMATCH | Aggregatable |
| E004 | TFM_INCOMPATIBLE | Aggregatable |
| E005 | NO_LOCAL_SOURCE | Aggregatable |
| E006 | CACHE_CORRUPT | Aggregatable |
| E007 | MSBUILD_NOT_FOUND | Fatal |
| E008 | GIT_ENVIRONMENT_ERROR | Fatal |
| E009 | CONFIG_INVALID | Aggregatable |
| E010 | BUILD_FAILED | Fatal |
| E011 | TEST_FAILED | Aggregatable |
| E012 | APICOMPAT_NOT_AVAILABLE | Aggregatable |
| E013 | NBGV_NOT_FOUND | Fatal |
| E014 | VERSION_PARSE_INVALID | Aggregatable |
| E999 | INTERNAL_ERROR | Fatal |

> **Relationship to `RetainedReason`:** The swap engine (see `reference-swap` spec, RS-01) uses a separate `RetainedReason` enumeration with overlapping names (`VERSION_MISMATCH`, `TFM_INCOMPATIBLE`, `NO_LOCAL_SOURCE`, `CYCLE_PREVENTION`, `TRANSITIVE_FLOOR_UNSATISFIED`). A `RetainedReason` is a planning-time classification describing why a swap was not performed; it is NOT a `TitiError` and does NOT by itself produce an error code. A `TitiError` is raised only when a bypassable safety check fails AND that check is NOT in the `overrides` set. `RetainedReason.NO_LOCAL_SOURCE` is purely informational (no local source candidate exists), is never bypassable, and never produces E005.
>
> | RetainedReason | In `overrides`? | Emitted `TitiError` code |
> |---|---|---|
> | `VERSION_MISMATCH` | no | E003 |
> | `VERSION_MISMATCH` | yes | none (warn diagnostic) |
> | `TFM_INCOMPATIBLE` | no | E004 |
> | `TFM_INCOMPATIBLE` | yes | none (warn diagnostic) |
> | `CYCLE_PREVENTION` | no | E002 (via `CycleReport`) |
> | `CYCLE_PREVENTION` | yes | none (warn diagnostic) |
> | `TRANSITIVE_FLOOR_UNSATISFIED` | no | none (diagnostic; no dedicated code) |
> | `TRANSITIVE_FLOOR_UNSATISFIED` | yes | none (warn diagnostic) |
> | `NO_LOCAL_SOURCE` | n/a (never bypassable) | none (informational; never E005) |
>
> E005 (`NO_LOCAL_SOURCE`) is reserved in the taxonomy for symmetry but is not raised by the swap engine; `titi check` (CLI-08) raises E003/E004 when a project is reported as incompatible.

> **E010 and E011 — build and test failures:** E010 (`BUILD_FAILED`) remains reserved for future build capabilities and SHALL NOT be raised by the current CLI surface. E011 (`TEST_FAILED`) is raised by the `test-detection` capability (see `test-detection` spec, TD-01 "VSTest failure") when `dotnet test --list-tests` fails (non-zero exit, build failure, or missing project) and by `titi tests record` when a test run fails. E011 is aggregatable: when multiple test projects fail during `titi tests record`, all failures are collected and reported together before the command exits with code 1.

> **E009 vs E014:** E009 (`CONFIG_INVALID`) is reserved for errors in `titi.config.edn`. E014 (`VERSION_PARSE_INVALID`) is raised when a version string fails to parse (see `domain-model` spec, DM-02 and DM-04).

Additional error codes SHALL NOT be added without a corresponding spec update to this taxonomy.

#### Scenario: Known error code emitted
- **WHEN** the graph build fails due to a malformed .csproj
- **THEN** the error carries code E001

#### Scenario: Unknown error not silenced
- **WHEN** an unexpected internal exception occurs that does not map to any specific error code (E001–E014)
- **THEN** the exception is wrapped in a TitiError with code E999 (INTERNAL_ERROR), the exception message as `message`, and the current `phase` in the context block, rather than producing an unformatted stack trace in production output

#### Scenario: ApiCompat not available
- **WHEN** `titi version detect` requires API compatibility analysis but `Microsoft.DotNet.ApiCompat.Tool` is not installed or the baseline assembly cannot be obtained
- **THEN** the error carries code E012 with a suggestion to install the ApiCompat tool or ensure the baseline package version is available in the configured NuGet feed

#### Scenario: VSTest enumeration failure
- **WHEN** `dotnet test --list-tests` fails (non-zero exit, build failure, or missing project) during `titi tests list` or `titi tests record`
- **THEN** the error carries code E011 (TEST_FAILED) with the underlying error text, and the failure is reported alongside any other test-project failures before the command exits
