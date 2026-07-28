## ADDED Requirements

### Requirement: Test Detection Configuration (CF-07)

The system SHALL parse a `TestDetectionConfig` sub-section of `TitiConfig` with the following fields:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `enabled` | boolean | `false` | Master toggle for per-test selection; when false, all operations fall back to project-level |
| `vstest-path` | string | `"dotnet"` | Path to the .NET CLI for VSTest operations |
| `collect-coverage` | boolean | `false` | Whether to collect code coverage on `titi tests record`; set automatically on first recording |
| `coverage-format` | keyword | `:cobertura` | Coverage output format (`:cobertura` or `:opencover`) |
| `cache-dir` | string | `".titi/test-cache"` | Root directory for all test-related caches: edges (`<cache-dir>/edges/`), items (`<cache-dir>/items/`), and run history (`<cache-dir>/history.edn`) |
| `fallback-threshold` | float [0,1] | `0.7` | Minimum confidence ratio below which selection falls back to project-level |
| `always-run-eviction-threshold` | integer | `5` | Consecutive passes before a test is evicted from the always-run set |
| `batch-size` | integer | `100` | Number of tests per `dotnet test --filter` batch when filter string exceeds the length limit |
| `exclude-patterns` | string[] | `[]` | Glob patterns for test methods to exclude from discovery (e.g. `["*Integration*"]`) |

> **Cache directory structure:** All test-detection caches live under a single `cache-dir` root:
> ```
> <cache-dir>/                  # default: .titi/test-cache/
>   edges/                      # test-to-source edge cache (DG-10)
>   items/                      # VSTest discovery cache per project
>   history.edn                 # run history persistence (TD-06, EDN format)
> ```
> This ensures a single configurable path controls all test cache artifacts, avoiding the proliferation of independent cache paths noted in earlier design review.

#### Scenario: Default config when section absent
- **GIVEN** a `titi.config.edn` with no `test-detection` section
- **WHEN** any `titi tests *` command is invoked
- **THEN** the system applies built-in defaults: `enabled=false`, `vstest-path="dotnet"`, `fallback-threshold=0.7`, `cache-dir=".titi/test-cache"`, and emits an info-level diagnostic suggesting the user enable test detection in config

#### Scenario: Custom fallback threshold
- **GIVEN** a config with `{:test-detection {:fallback-threshold 0.9}}`
- **WHEN** selection confidence is computed
- **THEN** fallback triggers when fewer than 90% of changed files have coverage edges

#### Scenario: Exclude pattern applied
- **GIVEN** a config with `{:test-detection {:exclude-patterns ["*Integration*"]}}`
- **WHEN** `titi tests list` enumerates test methods
- **THEN** any test whose method name or class name matches `*Integration*` is excluded from discovery results
