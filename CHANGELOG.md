# Changelog

## 0.3.1 — 2026-08-05

### Chores
- Embed README in NuGet package with AI-Assisted Development notice.

## 0.3.0 — 2026-08-05

### Features
- **PackageReference graph integration** — GraphBuilder now resolves internal
  PackageReferences to Binary-mode edges, detecting consumers of packages that
  use NuGet references instead of ProjectReferences in binary-mode monorepos.
- **Version management** — Full VN epic: NBGV integration, CPM detection, lock
  file management, NuGet 6.12 regression workaround, AssemblyVersion validation,
  cascading bump algorithm, baseline assembly acquisition.
- **Confidence model** — Weighted scoring (resolution × 0.6 + freshness × 0.25 +
  depth × 0.15) replacing the naive project-count / file-count ratio.
- **Static edge analysis** — L1+L2 and L3 method-level call graph for static
  test-to-source edges without coverage data.
- **Swap engine** — Conditional ProjectReference injection via Swap.targets,
  non-standard directory layout support.

### Bug Fixes
- **Subprocess safety** — Restore and test-discovery call sites now route
  through `RunProcess` with concurrent async drain, bounded timeout, and
  process-tree termination (could hang or orphan `dotnet` children).
- **Confidence scoring** — False exit 10 on multi-file single-project changes
  fixed by routing through the weighted model.
- **Graph edge orientation** — Fixed dependents direction in affected analysis
  for GraphBuilder-style edges.
- **CI** — AOT binary loading, lock file drift, test filtering.

### Chores
- Removed unused ClojureCLR dependency (dead weight since initial skeleton).
- Added `dont check` to pre-push hook.
- 433 tests passing.