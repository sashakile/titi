---
title: CLI Reference
---

# CLI Reference

## Usage

```
titi <command> [options]
```

---

## `titi open <package-id>`

Generate a transient `.slnx` solution with one project's transitive dependency closure
swapped from binary (NuGet) to source (ProjectReference).

The swap retains the original `PackageReference` with `ExcludeAssets=All` and injects a
`ProjectReference` alongside, controlled by `InTitiContext=true`.

**Example:**

```bash
$ titi open Orion.Core.Data
{
  "solutionPath": ".titi/solutions/Orion.Core.Data.slnx",
  "swapped": [
    { "packageId": "Orion.Core.Data", "localSourcePath": "/repo/Orion.Core.Data/Orion.Core.Data.csproj" }
  ],
  "retained": [],
  "projectCount": 1
}
```

Open the generated `.slnx` in your IDE or build it with `dotnet build`.

---

## `titi affected [--base <ref>]`

List projects affected by current changes. Runs `git diff` between refs, maps changed
files to projects via the dependency graph, and computes transitive impact. With test
edges available, includes per-test selection results and confidence score.

**Flags:**
- `--base <ref>` — Base git ref to diff against (default: `HEAD~1`)

**Example:**

```bash
$ titi affected --base HEAD~3
{
  "changedFiles": ["src/Orion.Core.Data/Parser.cs"],
  "directlyAffected": [{ "packageId": "Orion.Core.Data", "path": "..." }],
  "transitivelyAffected": [{ "packageId": "Orion.App", "path": "..." }],
  "totalAffected": 2,
  "selectedTests": [...],
  "confidence": 1.0
}
```

---

## `titi tests list <csproj>`

Enumerate test items in a project. Invokes `dotnet test --list-tests` and parses the
console output into `TestItem` records. Detects xUnit, NUnit, and MSTest from executor URI.

Results are cached in `.titi/test-cache/items/<package>/items.json`, keyed by a
content fingerprint of the `.csproj` + all `.cs` files. Subsequent runs skip
`dotnet test` when the fingerprint is unchanged; editing a source file invalidates
the cache automatically.

**Example:**

```bash
$ titi tests list tests/Orion.UnitTests/Orion.UnitTests.csproj
{ "tests": [{ "testId": "...", "className": "...", "methodName": "...", "framework": "xunit", "tier": "unit" }] }
```

---

## `titi tests ingest <trx> [--coverage <cobertura>]`

Parse TRX test results and Cobertura coverage XML, building `TestToSourceEdge` records
and caching them in `.titi/test-cache/edges/edges.edn`.

**Flags:**
- `--coverage <path>` — Path to Cobertura coverage XML (enables edge building)

**Error handling:** If the TRX file is unparseable, titi exits `1` and **leaves the
existing edge index untouched** — a malformed ingest never overwrites prior good data.

---

## `titi tests record`

Run all test projects with coverage collection, then ingest results.

**Incremental:** Only projects whose source fingerprint changed since the last
`record` are re-run. Edges for unchanged projects are preserved from
`.titi/test-cache/edges/projects/<package>.edn`. Run `titi clean` to force a
full re-record.

---

## `titi test-manifest [--tier <tier>] [--select] [--list]`

Generate a Traversal `.proj` for affected tests.

**Flags:**
- `--tier <tier>` — Filter by test tier (`unit`, `package`, `integration`, `compatibility`)
- `--select` — Generate per-test filtered Traversal (requires edge cache)
- `--list` — Print selected test IDs to stdout (implies `--select`)
- `--base <ref>` — Base git ref to diff against (default: `HEAD~1`)

**Exit codes:**

| Code | Meaning |
|------|---------|
| `0` | Success — selected tests returned |
| `10` | Confidence below threshold — consider full-suite run |
| `20` | Safe to skip — no affected tests |

**Example:**

```bash
$ titi test-manifest --select --list
Orion.UnitTests.ParserTests.TestParse
Orion.UnitTests.ParserTests.TestParseEmpty
$ echo $?
0
```

---

## `titi testaruda-adapter`

Start the testaruda adapter (JSON-over-stdio protocol). Reads commands from stdin,
writes responses to stdout. See the [Adapter Protocol](adapter.md) page for details.

---

## `titi repl`

Interactive dependency graph REPL. Commands: `deps`, `dependents`, `path`, `info`,
`affected`, `tree`, `help`, `quit`/`exit`.

**Example:**

```
$ titi repl
> deps Orion.Core.Data
Orion.Auth
Orion.Storage
> tree Orion.App --depth 2
Orion.App
├── Orion.Core.Data
│   └── Orion.Auth
└── Orion.Storage
```

---

## `titi clean`

Remove all titi-generated artifacts (`.titi/` directory).

---

## `titi --help`

Show usage information.
