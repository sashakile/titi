## 1. Pre-implementation (blocking)
- [x] 1.1 Resolve Decision 4 (component-graph scope for `static-deps`) — RESOLVED: whole-repo (option b) confirmed against testaruda v0.2.3 store/engine architecture (see design.md Decision 4)
- [x] 1.2 Confirm Decision 2 (adapter process-lifetime model) against testaruda's actual `AdapterIO` implementation — RESOLVED: long-lived model confirmed against `src/adapter.rs` (v0.2.3, commit `34e8db6`)
- [ ] 1.3 Benchmark titi's CLR process cold-start time (AOT vs. non-AOT) to inform minimum adapter timeout — also review testaruda's `add-coldstart-classification` change outcome if available, to align titi's timeout model with testaruda's coldstart direction
- [x] 1.4 Create beads issues to track the two blocking decisions through resolution, then create implementation issues for each Phase 1 task — blocking-decision issues (`titi-euu`, `titi-7qw`) created and closed; epic (`titi-dik`) and external testaruda-side config blocker (`titi-co9`) created; SEQ-1 (`titi-2bz`) resolved and closed

## 2. Core adapter subcommand
- [x] 2.1 Implement handshake handler: advertise `titi`, languages `["csharp"]`, granularity `project`, `{symbol_model_complete: false}`
- [x] 2.2 Implement `discover` handler: emit one test item per project where `ProjectDescriptor.isTestProject = true`
- [x] 2.3 Implement `static-deps` handler: reuse `AffectedSet` (DG-04), K-value = multiplicative identity
- [x] 2.4 Implement `fingerprint` handler: reuse `FileFingerprint` (DG-07) without changes
- [x] 2.5 Implement `run-args` handler: reuse `titi test-manifest` Traversal-.proj generator (CLI-06)
- [x] 2.6 Implement long-lived process lifecycle: build or load graph once on start, answer all commands from in-memory state, handle graph-build failure (emit diagnostic, exit non-zero so testaruda falls back to all-tests per TIA-ADAPT-012)

## 3. TRX ingestion (ingest)
- [x] 3.1 Implement TRX output parsing to report per-test PASS/FAIL/duration
- [x] 3.2 Wire `ingest` handler to TRX parser, relying on TIA-ADAPT-012 fallback for malformed input

## 4. Wiring and registration
- [x] 4.1 Register `titi testaruda-adapter` in titi's CLI command dispatch alongside existing commands
- [x] 4.2 Ensure the new subcommand does not affect any existing command dispatch when not invoked

## 5. Testing
- [x] 5.1 Create fixture: synthetic .NET monorepo (2–3 projects, 1 test project) — already exists from add-test-item-detection task 7.1
- [x] 5.2 Verify fixture produces same affected set through both `titi test-manifest` and testaruda's engine via the adapter (integration test written, skipped by default — run with `dotnet test --filter Category=Integration`)
- [x] 5.3 Verify rollback: removing the adapter subcommand leaves all existing titi commands functional (tested — `titi affected`, `titi tests`, `titi test-manifest`, `titi clean` all work identically without adapter)
- [x] 5.4 Document fixture maintenance: regenerate fixture when DG-01 or DG-04 logic changes

## 6. Documentation
- [x] 6.1 Document adapter's known limitations (process lifetime, lock interaction, F#/VB.NET status)
- [x] 6.2 Document minimum adapter timeout setting in testaruda's config for this adapter
- [x] 6.3 Record decision outcomes from 1.1 and 1.2 in design.md — already documented in design.md Decisions 2 and 4

## 7. Phase 2 (deferred — not in this change)
- [ ] (Future) VSTest `--list-tests` + TRX-based method-level `discover`/`static-deps`
- [ ] (Future) Upgrade handshake to finer granularity with `symbol_model_complete: true`
