---
title: testaruda Adapter
---

# testaruda Adapter

titi ships a built-in testaruda adapter subcommand (`titi testaruda-adapter`)
that speaks the testaruda JSON-over-stdio adapter protocol. This allows
[testaruda](https://github.com/charly-vibes/testaruda) to use titi's
MSBuild-accurate project graph as its C#/.NET static-dependency source.

## Protocol

The adapter reads one JSON request per line from stdin and writes one JSON
response per line to stdout. Each request has a `command` field and `params`
object.

### Commands

| Command | Description | Params |
|---------|-------------|--------|
| `handshake` | Capability advertisement | `{}` |
| `discover` | Enumerate test projects | `{}` |
| `static-deps` | Compute affected test items | `changed_files`, `affected_projects` |
| `fingerprint` | Return project fingerprints | `{}` |
| `run-args` | Generate test command | `test_ids` |
| `ingest` | Parse TRX results | `trx_path` |
| `shutdown` | Graceful shutdown | `{}` |

## Phase 1: Project-level granularity

Phase 1 operates at project granularity: each test item is a whole test
assembly. `symbol_model_complete` and `runtime_edges` are both `false`.
Benefits are composability, caching, and confidence scoring — not raw
test-count reduction.

## Known Limitations

- **Process lifetime**: The adapter is a long-lived process for the duration of
  one testaruda invocation. It builds the `MonorepoGraph` once during handshake
  and answers all commands from in-memory state.
- **Lock interaction**: The adapter holds a read-only reference to the graph
  cache. Run `titi cache warm` before invoking testaruda to avoid contention.
- **Framework detection**: Phase 1 hardcodes `xunit` as the default framework
  for all test projects. NUnit/MSTest projects are treated as xUnit at the
  project level.
- **CLR cold-start**: The .NET CLR process startup time is a distinct cost
  from the graph-build budget. Minimum adapter timeout: 30s for small repos,
  60s for large repos.

## Configuration

The adapter requires no additional titi configuration — it reuses the existing
`titi.config.edn` and `.titi/graph.cache`. The testaruda-side config defaults
are in the testaruda repository (v0.2.5+).
