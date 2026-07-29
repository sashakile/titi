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
response per line to stdout. Each request has a `command` field and a `params`
object.

### Example: handshake

```
→ {"command":"handshake","params":{}}
← {"result":{"name":"titi","version":"0.1.0","protocol":1,"languages":["csharp"],"granularity":"method","capabilities":{"symbol_model_complete":true,"fingerprinting":true,"runtime_edges":false}}}
```

### Example: discover

```
→ {"command":"discover","params":{}}
← {"result":[{"node_id":"LibTest.MyTests.Test1","suite_kind":"LibTest.MyTests.Test1","file":"/asm/LibTest.dll"}]}
```

The response uses the canonical testaruda adapter protocol format: `result` is a direct array of items with `node_id`, `suite_kind`, and `file` fields. `suite_kind` is the fully-qualified `ClassName.MethodName`.

### Commands

| Command | Description | Params |
|---------|-------------|--------|
| `handshake` | Capability advertisement | `{}` |
| `discover` | Enumerate test items (method-level) | `{}` |
| `static-deps` | Compute affected test items | `changed_files`, `affected_projects` |
| `fingerprint` | Return project fingerprints | `{}` |
| `run-args` | Generate test command | `test_ids` |
| `ingest` | Parse TRX results | `trx_path`, `cobertura_path` |
| `shutdown` | Graceful shutdown | `{}` |

## Granularity

The adapter operates at **method-level granularity**: each test item is a
single test method, not a whole assembly. The handshake advertises
`symbol_model_complete: true` and `runtime_edges: false`, meaning titi
provides a complete static symbol model but does not trace runtime call edges.

## Known Limitations

- **Process lifetime**: The adapter is a long-lived process for the duration of
  one testaruda invocation. It builds the `MonorepoGraph` once during handshake
  (in-memory) and answers all commands from that in-memory state. There is no
  persistent graph cache file.
- **Framework detection**: The adapter reports `xunit` as the framework for all
  test projects at the project level. NUnit/MSTest projects are normalized to
  `xunit` in the handshake; method-level framework detection uses the VSTest
  executor URI.
- **CLR cold-start**: The .NET CLR process startup time is a distinct cost
  from the graph-build budget. Minimum adapter timeout: 30s for small repos,
  60s for large repos.

## Configuration

The adapter requires no additional titi configuration — it reuses the existing
`titi.config.edn` and builds the project graph from source at startup. The
testaruda-side config defaults (`.csproj`/`.sln`/`.slnx` →
`titi testaruda-adapter` mapping, dotnet-project detection) are in the
testaruda repository (v0.2.5+).
