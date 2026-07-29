# Configuration

## Purpose

The configuration capability defines how titi reads, validates, and exposes its `titi.config.json` file. The loader supports a limited set of root-level fields (prefix, source-roots, version-policy, and test-detection flags); all other top-level keys are rejected with E009. Aspirational sections (cache, test-tiers, ide, ci) are not currently consumed and will be added in future releases.

## Requirements

### Requirement CF-01: Config File Discovery

The system SHALL locate `titi.config.json` by walking up from the current working directory to the git repository root, using the first file found.

#### Scenario: Config found at repo root
- **GIVEN** `titi.config.json` exists at the git root
- **WHEN** any titi command is invoked from a subdirectory
- **THEN** the config is loaded from the repo root

#### Scenario: No config file found
- **WHEN** no `titi.config.json` exists anywhere in the directory ancestry
- **THEN** the system applies built-in defaults (versionPolicy=SEMVER_COMPATIBLE, sourceRoot=["src/"]) and emits a warn-level diagnostic suggesting the user run `titi init` to create an explicit config file

#### Scenario: Current directory not in a git repository
- **WHEN** config discovery is attempted and the current working directory is not inside any git repository
- **THEN** the system applies built-in defaults, emits a warn-level diagnostic suggesting the user run `titi init` inside a git repo, and does NOT raise E008 (GIT_ENVIRONMENT_ERROR) at config-discovery time; E008 is raised later only by graph-dependent commands (see `dependency-graph` spec, DG-01)

### Requirement CF-02: Core Configuration Fields

The system SHALL parse the `TitiConfig` root with the following consumed fields:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `prefix` | string | `""` | Namespace prefix for package/version detection |
| `source-root` | string | — | Single source root path relative to repo root (singular, backwards-compatible) |
| `sourceRoot` | string | — | Alias for `source-root` (camelCase backwards-compatible form) |
| `source-roots` | array of strings | `["src/"]` | Multi-root alternative: list of repo-relative source directories |
| `version-policy` | string | `"semver-compatible"` | Version policy: `"strict"`, `"semver-compatible"`, or `"force"` |
| `versionPolicy` | string | — | Alias for `version-policy` (camelCase backwards-compatible form) |
| `test-detection-enabled` | boolean | `false` | Enable test-scope-from-coverage detection algorithm |
| `fallback-threshold` | number | `0.7` | Confidence threshold (0–1) for fallback to full project set |

When `source-roots` (plural array) is present it takes precedence; `source-root`/`sourceRoot` (singular string) is used only when the plural form is absent. If neither is provided the default `["src/"]` applies.

Any top-level key not listed above SHALL be rejected with E009 listing every unsupported key in a single error message.

#### Scenario: Valid minimal config
- **GIVEN** a config file with only `prefix`
- **WHEN** the config is loaded
- **THEN** all consumed fields use their defaults and no error is raised

#### Scenario: Unsupported top-level keys
- **GIVEN** a config file containing unsupported sections like `cache`, `test-tiers`, `ide`, or `ci`
- **WHEN** the config is loaded
- **THEN** E009 is emitted listing every unsupported key, and the config is rejected

### Requirement CF-03: Test Detection Configuration (TID-6)

The system SHALL read `test-detection-enabled` (boolean) and `fallback-threshold` (number, 0–1) from the config root. When `test-detection-enabled` is `true`, the system activates coverage-driven test selection with the configured fallback threshold. Values of `fallback-threshold` outside the [0, 1] range SHALL be rejected with E009.

> **Note:** The full `TestDetectionConfig` record includes additional fields (vstest-path, collect-coverage, coverage-format, cache-dir, always-run-eviction-threshold, batch-size, exclude-patterns) that are currently hard-coded to defaults and not user-configurable. They will be exposed as config fields in a future release.

#### Scenario: test-detection-enabled true with custom threshold
- **GIVEN** `test-detection-enabled: true` and `fallback-threshold: 0.9`
- **WHEN** the config is loaded
- **THEN** test detection is enabled with the custom threshold

#### Scenario: fallback-threshold outside valid range
- **GIVEN** `fallback-threshold: -0.1` or `fallback-threshold: 1.5`
- **WHEN** the config is loaded
- **THEN** E009 is emitted and the config is rejected

#### Scenario: test-detection-enabled false (default)
- **GIVEN** no `test-detection-enabled` in config
- **WHEN** the config is loaded
- **THEN** test detection is disabled, using defaults

### Requirement CF-04: Config Validation

The system SHALL validate the loaded config on startup. The following validations SHALL be performed:

- **Unknown top-level keys**: any key not listed in CF-02 is rejected with E009 listing all unsupported keys
- **fallback-threshold range**: rejected with E009 if outside [0, 1]
- **Source-root path warnings**: absolute paths and non-existent directories produce a warn-level diagnostic but do not prevent loading

When the config file itself is not valid JSON, the system SHALL emit E009 with a parse error message.

#### Scenario: Multiple validation issues reported
- **GIVEN** a config with an unsupported key AND an invalid `fallback-threshold` value
- **WHEN** the config is loaded
- **THEN** E009 is emitted with the first validation failure encountered
