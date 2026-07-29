# Change: Add DocFX documentation site with GitHub Pages deployment

## Why

titi has no documentation beyond a single `README.md`. Contributors and users must read source code or openspec specs to understand commands, architecture, and safety model. A proper documentation site provides:

- API reference generated from C# XML doc comments
- Hand-written guides (CLI reference, architecture, safety model)
- Openspec specs browsable alongside the docs
- GitHub Pages deployment without a `gh-pages` branch (artifact-based)

## What Changes

- Add `docfx.json` configuration for DocFX v2
- Add `docs/` directory with hand-written markdown content (index, CLI reference, architecture, safety)
- Add `docs/specs/` symlink or copy of `openspec/specs/` for browsable specs
- Add `.github/workflows/docs.yml` for GHA build + Pages deployment
- Add `just docs` recipe for local preview
- Add `dotnet tool manifest` + `dotnet tools.json` for DocFX tool dependency

## Impact

- Affected specs: N/A (new capability — no existing spec modified)
- Affected code: `docs/`, `.github/workflows/docs.yml`, `.config/dotnet-tools.json`
- No breaking changes to existing commands or APIs
