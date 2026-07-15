## 1. Pre-implementation (blocking)
- [ ] 1.1 Resolve Decision 4 (component-graph scope for `static-deps`) — determine whether adapter returns whole-repo or per-component affected sets
- [ ] 1.2 Confirm Decision 2 (adapter process-lifetime model) against testaruda's actual `AdapterIO` implementation
- [ ] 1.3 Benchmark titi's CLR process cold-start time (AOT vs. non-AOT) to inform minimum adapter timeout
- [ ] 1.4 Create beads issues to track the two blocking decisions through resolution, then create implementation issues for each Phase 1 task

## 2. Core adapter subcommand
- [ ] 2.1 Implement handshake handler: advertise `titi`, languages `["csharp"]`, granularity `project`, `{symbol_model_complete: false}`
- [ ] 2.2 Implement `discover` handler: emit one test item per project where `ProjectDescriptor.isTestProject = true`
- [ ] 2.3 Implement `static-deps` handler: reuse `AffectedSet` (DG-04), K-value = multiplicative identity
- [ ] 2.4 Implement `fingerprint` handler: reuse `FileFingerprint` (DG-07) without changes
- [ ] 2.5 Implement `run-args` handler: reuse `titi test-manifest` Traversal-.proj generator (CLI-06)
- [ ] 2.6 Implement long-lived process lifecycle: build or load graph once on start, answer all commands from in-memory state, handle graph-build failure (emit diagnostic, exit non-zero so testaruda falls back to all-tests per TIA-ADAPT-012)

## 3. TRX ingestion (ingest)
- [ ] 3.1 Implement TRX output parsing to report per-test PASS/FAIL/duration
- [ ] 3.2 Wire `ingest` handler to TRX parser, relying on TIA-ADAPT-012 fallback for malformed input

## 4. Wiring and registration
- [ ] 4.1 Register `titi testaruda-adapter` in titi's CLI command dispatch alongside existing commands
- [ ] 4.2 Ensure the new subcommand does not affect any existing command dispatch when not invoked

## 5. Testing
- [ ] 5.1 Create fixture: synthetic .NET monorepo (2–3 projects, 1 test project)
- [ ] 5.2 Verify fixture produces same affected set through both `titi test-manifest` and testaruda's engine via the adapter
- [ ] 5.3 Verify rollback: removing the adapter subcommand leaves all existing titi commands functional
- [ ] 5.4 Document fixture maintenance: regenerate fixture when DG-01 or DG-04 logic changes

## 6. Documentation
- [ ] 6.1 Document adapter's known limitations (process lifetime, lock interaction, F#/VB.NET status)
- [ ] 6.2 Document minimum adapter timeout setting in testaruda's config for this adapter
- [ ] 6.3 Record decision outcomes from 1.1 and 1.2 in design.md

## 7. Phase 2 (deferred — not in this change)
- [ ] (Future) VSTest `--list-tests` + TRX-based method-level `discover`/`static-deps`
- [ ] (Future) Upgrade handshake to finer granularity with `symbol_model_complete: true`
