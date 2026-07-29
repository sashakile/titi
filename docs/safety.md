---
title: Safety Model
---

# Safety Model

**TL;DR:** titi's test-impact analysis uses a multi-factor safety model with an always-run set (failed/new/no-history tests), confidence scoring (resolution + freshness + depth), and fallback to project-level when confidence drops below threshold.

titi's test selection uses a multi-factor safety model to minimize the risk of missed
regressions while maximizing test execution savings.

## Always-Run Set

Certain tests are always selected regardless of coverage edges:

- **Failed tests**: tests that failed in the last run are always re-run
- **Newly added tests**: tests not seen before are always re-run
- **No-history tests**: tests with no prior run history are always re-run

## Confidence Scoring

When test-to-source edges are available, confidence is a weighted combination
of three factors:

| Factor | Weight | Formula |
|--------|--------|---------|
| Changed-file resolution | 60% | `resolvedFiles / changedFiles` — ratio of changed files mapped to affected projects |
| Edge freshness | 25% | `min(1.0, edgeFreshness)` — 1.0 if edges from current run, decaying toward 0 |
| History depth | 15% | `min(1.0, historyDepth / 10.0)` — saturates at 10 prior runs per test |

**Combined:** `confidence = resolution × 0.6 + freshness × 0.25 + depth × 0.15`

When there are no changed files (`changedFiles.Length == 0`), confidence
defaults to `1.0` (no diff → no risk of missing a regression).

## Fallback

When confidence drops below the configured threshold (default `0.7`), selection
falls back to project-level granularity (all tests in affected projects).

The threshold is configurable in `titi.config.json` under
`test-detection.fallback-threshold` (default `0.7`). A stricter value (e.g.
`0.85`) is recommended for release branches.

## Exit Codes

The `test-manifest --select --list` command uses exit codes to signal safety:

| Exit Code | Meaning |
|-----------|---------|
| `0` | Success — selected tests returned |
| `10` | Confidence below threshold — consider full-suite run |
| `20` | Safe to skip — no tests selected |

## Missed-Selection Incidents

When a test is missed by selection but should have been selected, the incident
is recorded (`Safety.RecordMissedSelection`) and can promote the edge weight
for future selections — the system learns from regressions to improve future
selection accuracy.
