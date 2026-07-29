## Context

titi is a .NET CLI tool with a growing surface area (15+ commands, test-impact analysis, adapter protocol). The project uses openspec for spec-driven development. Documentation should be:

- **Discoverable**: hosted on GitHub Pages, linked from README
- **Comprehensive**: API reference + guides + specs
- **Maintainable**: markdown-driven, CI-published, no `gh-pages` branch
- **Local-previewable**: `just docs` should serve the site

DocFX is the standard .NET documentation tool. It generates API reference from C# XML doc comments and integrates hand-written markdown via a `docfx.json` manifest.

## Goals / Non-Goals

### Goals
- DocFX site with API reference (from `src/`) + hand-written guides (from `docs/`)
- Openspec specs embedded as browsable reference
- GitHub Pages deployment via artifact-based deployment (no `gh-pages` branch)
- Local preview via `just docs`
- CI build + deploy on push to `main`

### Non-Goals
- Documentation for testaruda adapter protocol (that lives in testaruda's docs)
- Versioned documentation (single `latest` site at project root)
- Search index (DocFX default search is sufficient)

## Decisions

### D1: DocFX v2 (DotnetTool) over docfx-console container

- **Decision**: Use `dotnet tool install docfx` (v2) as a project-local tool via `.config/dotnet-tools.json`
- **Rationale**: No Docker dependency, consistent with .NET ecosystem, works in CI without additional setup
- **Alternatives**: `docfx` docker image — heavier, requires Docker daemon in CI

### D2: Copy openspec specs into docs/ rather than symlink

- **Decision**: Copy `openspec/specs/` into `docs/specs/` at build time via a `just docs` recipe step
- **Rationale**: DocFX needs `.md` files under the `docs/` content root; symlinks have edge cases in CI (Git LFS, checkout depth)
- **Alternative**: Symlink — simpler but fragile across platforms

### D3: Artifact-based Pages deployment (modern GH Pages)

- **Decision**: Use `actions/upload-pages-artifact` + `actions/deploy-pages` instead of `gh-pages` branch
- **Rationale**: No `gh-pages` branch to maintain, no force-push risk, native GH Pages support since 2022
- **Reference**: https://github.com/actions/deploy-pages

### D4: Single `just docs` recipe for both preview and CI

- **Decision**: `just docs` copies specs, builds the site; `just docs --serve` adds `--serve` for local preview
- **Rationale**: Single source of truth; CI calls `just docs` (without `--serve`) and then uploads the output

## Risks / Trade-offs

- **DocFX version drift**: Pin the major version in `.config/dotnet-tools.json` to avoid unexpected breaking changes
- **Spec staleness**: The copy step is manual at build time — if specs are updated but docs aren't rebuilt, the site is stale. Mitigated by CI building on every push to `main`
- **Repo size**: `docs/specs/` is a copy, not a symlink, so it appears in git diff. Use `.gitignore` to exclude it from version control and regenerate on build

## Open Questions

- Should the README badge link to the GH Pages URL?
- Should the site be deployed to `https://<org>.github.io/titi` or a custom domain?
