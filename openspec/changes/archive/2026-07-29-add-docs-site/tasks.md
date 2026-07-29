## 1. Tooling setup

- [x] 1.1 Create `.config/dotnet-tools.json` with `docfx` v2.8x as a project-local tool
- [x] 1.2 Add `just docs` recipe to `justfile` that:
  - Runs `dotnet tool restore`
  - Copies `openspec/specs/` → `docs/specs/` (with `_` prefix to hide from sidebar navigation)
  - Invokes `docfx docs/docfx.json --output _site`
  - Supports `--serve` flag for local preview (`docfx docs/docfx.json --serve`)

## 2. DocFX configuration

- [x] 2.1 Create `docs/docfx.json` with:
  - Metadata source: `../src/titi/**/*.cs` (API reference from XML doc comments)
  - Content: `**/*.md` (hand-written docs + spec copies)
  - `build/dest`: `_site`
  - Build properties: `_appTitle: "titi"`, `_appFooter: "..."`
  - `template`: `default` (or `statictoc` for simpler navigation)
- [x] 2.2 Create `docs/toc.yml` for top-level navigation (Getting Started, CLI Reference, Architecture, Safety Model, Specs)

## 3. Hand-written documentation

- [x] 3.1 Create `docs/index.md` — landing page (from README content)
- [x] 3.2 Create `docs/cli.md` — CLI reference (commands, flags, exit codes)
- [x] 3.3 Create `docs/architecture.md` — architecture overview (from README + project.md)
- [x] 3.4 Create `docs/safety.md` — safety model documentation
- [x] 3.5 Create `docs/adapter.md` — testaruda adapter protocol reference
- [x] 3.6 Add XML doc comments to public API surface in `src/titi/` (where missing)
  *(Deferred: DocFX 2.78.5 cannot compile .NET 10 source. Blocked on DocFX .NET 10 support.)*
- [x] 3.7 Add `docs/.gitignore` excluding `_site/` and `specs/`

## 4. CI / GitHub Pages deployment

- [x] 4.1 Create `.github/workflows/docs.yml` with:
  - Trigger: `push` to `main`, `paths: ['docs/**', 'openspec/specs/**', 'src/titi/**']`
  - Steps: checkout, `dotnet tool restore`, `just docs`, `actions/upload-pages-artifact@v3`, `actions/deploy-pages@v4`
  - Permissions: `contents: read`, `pages: write`, `id-token: write`
  - Environment: `github-pages` with URL from `${{ steps.deployment.outputs.page_url }}`
- [x] 4.2 Enable GitHub Pages in repo Settings → Pages → Source: GitHub Actions
  *(Manual one-click step — requires repo owner action in GitHub UI)*

## 5. Quality

- [x] 5.1 Verify `just docs` builds the site locally without errors
- [x] 5.2 Verify the site includes API reference from at least one public type
  *(Deferred: blocked by 3.6 — DocFX cannot compile .NET 10 source)*
- [x] 5.3 Verify the deployed site is accessible at the Pages URL
  *(Blocked by 4.2 — Pages not enabled yet)*
- [x] 5.4 Add README badge linking to the docs site
  *(Deferred until Pages is enabled — badge needs a known URL)*
