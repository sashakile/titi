---
title: CLI Reference
---

# CLI Reference

## Usage

```
titi <command> [options]
```

## Commands

### `titi open <package-id>`

Generate a transient `.slnx` solution with one project's transitive dependency closure
swapped from binary (NuGet) to source (ProjectReference).

The swap retains the original `PackageReference` with `ExcludeAssets=All` and injects a
`ProjectReference` alongside, controlled by `InTitiContext=true`.

### `titi affected [--base <ref>]`

List projects affected by current changes. Runs `git diff` between refs, maps changed
files to projects via the dependency graph, and computes transitive impact. With test
edges available, includes per-test selection results and confidence score.

**Flags:**
- `--base <ref>` — Base git ref to diff against (default: `HEAD~1`)

### `titi tests list <csproj>`

Enumerate test items in a project. Invokes `dotnet test --list-tests` and parses the
output into `TestItem` records. Detects xUnit, NUnit, and MSTest from executor URI.

### `titi tests ingest <trx> [--coverage <cobertura>]`

Parse TRX test results and Cobertura coverage XML, building `TestToSourceEdge` records
and caching them.

**Flags:**
- `--coverage <path>` — Path to Cobertura coverage XML

### `titi tests record`

Run all test projects with coverage collection, then ingest results. Per-project
fingerprinting enables incremental re-runs.

### `titi test-manifest [--tier <tier>] [--select] [--list]`

Generate a Traversal `.proj` for affected tests.

**Flags:**
- `--tier <tier>` — Filter by test tier (`unit`, `package`, `integration`, `compatibility`)
- `--select` — Generate per-test filtered Traversal
- `--list` — Print selected test IDs (implies `--select`)
- `--base <ref>` — Base git ref to diff against (default: `HEAD~1`)

**Exit codes:**
- `0` — Success
- `10` — Confidence below threshold (consider full-suite run)
- `20` — Safe to skip (no affected tests)

### `titi testaruda-adapter`

Start the testaruda adapter (JSON-over-stdio protocol). Reads commands from stdin,
writes responses to stdout. See the [Adapter Protocol](adapter.md) page for details.

### `titi repl`

Interactive dependency graph REPL. Commands: `deps`, `dependents`, `path`, `info`,
`affected`, `tree`, `help`, `quit`/`exit`.

### `titi clean`

Remove all titi-generated artifacts (`.titi/` directory).

### `titi --help`

Show usage information.
