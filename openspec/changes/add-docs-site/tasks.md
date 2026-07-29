## 1. Tooling setup

- [ ] 1.1 Create `.config/dotnet-tools.json` with `docfx` v2.8x as a project-local tool
- [ ] 1.2 Add `just docs` recipe to `justfile` that:
  - Runs `dotnet tool restore`
  - Copies `openspec/specs/` → `docs/specs/` (with `_` prefix to hide from sidebar navigation)
  - Invokes `docfx docs/docfx.json --output _site`
  - Supports `--serve` flag for local preview (`docfx docs/docfx.json --serve`)

## 2. DocFX configuration

- [ ] 2.1 Create `docs/docfx.json` with:
  - Metadata source: `../src/titi/**/*.cs` (API reference from XML doc comments)
  - Content: `**/*.md` (hand-written docs + spec copies)
  - `build/dest`: `_site`
  - Build properties: `_appTitle: "titi"`, `_appFooter: "..."`
  - `template`: `default` (or `statictoc` for simpler navigation)
- [ ] 2.2 Create `docs/toc.yml` for top-level navigation (Getting Started, CLI Reference, Architecture, Safety Model, Specs)

## 3. Hand-written documentation

- [ ] 3.1 Create `docs/index.md` — landing page (from README content)
- [ ] 3.2 Create `docs/cli.md` — CLI reference (commands, flags, exit codes)
- [ ] 3.3 Create `docs/architecture.md` — architecture overview (from README + project.md)
- [ ] 3.4 Create `docs/safety.md` — safety model documentation
- [ ] 3.5 Create `docs/adapter.md` — testaruda adapter protocol reference
- [ ] 3.6 Add XML doc comments to public API surface in `src/titi/` (where missing)
- [ ] 3.7 Add `docs/.gitignore` excluding `_site/` and `specs/`

## 4. CI / GitHub Pages deployment

- [ ] 4.1 Create `.github/workflows/docs.yml` with:
  - Trigger: `push` to `main`, `paths: ['docs/**', 'openspec/specs/**', 'src/titi/**']`
  - Steps: checkout, `dotnet tool restore`, `just docs`, `actions/upload-pages-artifact@v3`, `actions/deploy-pages@v4`
  - Permissions: `contents: read`, `pages: write`, `id-token: write`
  - Environment: `github-pages` with URL from `${{ steps.deployment.outputs.page_url }}`
- [ ] 4.2 Enable GitHub Pages in repo Settings → Pages → Source: GitHub Actions

## 5. Quality

- [ ] 5.1 Verify `just docs` builds the site locally without errors
- [ ] 5.2 Verify the site includes API reference from at least one public type
- [ ] 5.3 Verify the deployed site is accessible at the Pages URL
- [ ] 5.4 Add README badge linking to the docs site
