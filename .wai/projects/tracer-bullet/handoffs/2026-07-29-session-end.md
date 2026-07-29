---
date: 2026-07-29
project: tracer-bullet
phase: plan
---

# Session Handoff

## What Was Done

- Audited source, architecture/operability, documentation/spec consistency,
  tests, and delivery workflows.
- Created triage epic `titi-k2x` with 15 evidence-backed child tickets.
- Recorded two grounded `dont` claims for the highest-risk correctness defects.
- Verified `just ci`: release build succeeded; 219 tests passed, one adapter
  integration test was skipped; all `dont` claims are grounded.

## Key Decisions

- Consolidated overlapping findings into one ownership boundary per ticket.
- Assigned `titi-k2x.2` P0 because incremental cache loss can silently omit
  tests. Assigned direct correctness, safety, adapter, and release gaps P1;
  bounded performance/reproducibility/docs maintenance findings are P2.
- Every child uses a red→green→refactor acceptance cycle.

## Gotchas & Surprises

- The first attempt to ground the long incremental-recording excerpt hit a
  `dont` parser error after creating the claim. Grounding two short excerpts
  repaired it; `dont check` now passes.
- The Rule of 5 command was not available in this environment.

## What Took Longer Than Expected

- Repairing the partially created `dont` claim required inspecting `dont list`
  and attaching shorter evidence excerpts.

## Open Questions

- Triage must choose whether canonical OpenSpec requirements should be
  implemented or narrowed to shipped behavior for CLI/configuration tickets.
- Safety triage must choose automatic project-level fallback versus an explicit
  caller-driven exit-code contract.

## Next Steps

1. Claim and implement P0 `titi-k2x.2`.
2. Address graph correctness `titi-k2x.1` before relying on affected selection.
3. Sequence remaining P1 correctness/adapter tickets, then delivery/spec work.

## Context

### git_status

```
M  .beads/issues.jsonl
 M .dont/db.cozo
?? .wai/projects/tracer-bullet/research/2026-07-29-2026-07-29-global-review-created-epic-titi-k2x-wi.md
```

### open_issues

```
○ titi-k2x ● P1 [epic] Triage global codebase review findings (2026-07-29)
├── ○ titi-k2x.2 ● P0 [bug] Preserve unchanged test edges and fingerprints during incremental recording
├── ○ titi-k2x.1 ● P1 [bug] Correct dependent-edge orientation in affected-project traversal
├── ○ titi-k2x.3 ● P1 [bug] Make dotnet subprocess timeouts enforceable and deadlock-safe
├── ○ titi-k2x.4 ● P1 [bug] Use bounded collision-resistant discovery-cache keys
├── ○ titi-k2x.5 ● P1 [bug] Prevent cold adapter startup from caching false empty discovery
├── ○ titi-k2x.6 ● P1 [bug] Honor adapter scope and terminate on shutdown
├── ○ titi-k2x.8 ● P1 [bug] Canonicalize coverage and changed-file paths before test selection
├── ○ titi-k2x.9 ● P1 [bug] Align confidence fallback behavior and threshold configuration
├── ○ titi-k2x.10 ● P1 Reconcile the canonical CLI specification with shipped commands
├── ○ titi-k2x.11 ● P1 Reconcile configuration specification with the implemented loader
├── ○ titi-k2x.12 ● P1 Run currently skipped end-to-end scenarios in CI
├── ○ titi-k2x.14 ● P1 Validate publish and package artifacts before release
├── ○ titi-k2x.7 ● P2 [bug] Distinguish an empty git diff from diff failure
├── ○ titi-k2x.13 ● P2 Enforce deterministic locked dependency restore
└── ○ titi-k2x.15 ● P2 Make documentation status and PR checks reflect repository reality

--------------------------------------------------------------------------------
Total: 16 issues (16 open, 0 in progress)

Status: ○ open  ◐ in_progress  ● blocked  ✓ closed  ❄ deferred
```
