---
title: Safety Model
---

# Safety Model

Test selection uses a multi-factor safety model to minimize the risk of missed
regressions while maximizing test execution savings.

## Always-Run Set

Certain tests are always selected regardless of coverage edges:

- **Failed tests**: tests that failed in the last run are always re-run
- **Newly added tests**: tests not seen before are always re-run
- **No-history tests**: tests with no prior run history are always re-run

## Confidence Scoring

When test-to-source edges are available, confidence is a weighted combination:

| Factor | Weight | Description |
|--------|--------|-------------|
| Changed-file resolution | 60% | Ratio of changed files mapped to affected projects |
| Edge freshness | 25% | How recently the coverage edges were recorded |
| History depth | 15% | How many prior runs inform the selection |

## Fallback

When confidence drops below the configured threshold (default `0.7`), selection
falls back to project-level granularity (all tests in affected projects).

## Exit Codes

The `test-manifest --select --list` command uses exit codes to signal safety:

| Exit Code | Meaning |
|-----------|---------|
| `0` | Success — selected tests returned |
| `10` | Confidence below threshold — consider full-suite run |
| `20` | Safe to skip — no tests selected |

## Missed-Selection Incidents

When a test is missed by selection but should have been selected, the incident
is recorded and can promote the edge weight for future selections.
