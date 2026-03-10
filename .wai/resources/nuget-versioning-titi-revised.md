# NuGet monorepo versioning: resolution traps and bundle design for titi

> **Two failure scenarios this document addresses:**
> 1. A consumer of interdependent internal libraries gets an unexpected version of a transitive package — or a runtime `MissingMethodException` — even though all package versions appeared correct at restore time.
> 2. Consumers must update N individual packages every time a related set of libraries ships, creating coordination overhead and version drift risk.
>
> The root cause of (1) is a fundamental disconnect between when NuGet resolves packages (restore time) and when the CLR loads assemblies (runtime), combined with a resolver algorithm that deliberately ignores newer patch versions. The solution to (2) is a bundle/metapackage strategy with specific trade-offs that titi can manage. Both problems have concrete mitigations that require understanding the underlying mechanics.

---

## Two phases that can disagree: restore time vs. load time

NuGet operates at **restore time** — resolving which package versions to use and writing the result to `project.assets.json`. The CLR operates at **load time** — deciding which assembly files to actually load from disk. These two phases use different version concepts and can produce different outcomes. Understanding this distinction is the foundation of everything in this document.

---

## NuGet's resolver deliberately picks the oldest valid patch

NuGet's core resolution rule for `PackageReference` projects is **lowest applicable version**: given a constraint like `>= 1.0.0` with versions 1.0.0, 1.0.1, and 1.1.0 available on the feed, NuGet resolves to **1.0.0** — not the latest. This is intentional. Before NuGet 2.8, the resolver picked the lowest major.minor but highest patch, which caused non-determinism across machines. The fix was to always select the lowest patch too.

> **Note on resolver versions:** The algorithm described below applies to both the legacy resolver (pre-NuGet 6) and the new resolver (NuGet 6.12+, default in .NET 9) for the common case. However, NuGet 6.12+ can produce different results for cousin dependencies when CPM transitive pinning is involved — see the CPM section for details.

A bare `<PackageReference Include="LibB" Version="1.0.0" />` does not mean "use exactly 1.0.0." It means `>= 1.0.0`. To pin exactly, you must use bracket notation: `[1.0.0]`. This misunderstanding is the root of many monorepo versioning issues.

Four rules govern PackageReference resolution, applied in priority order:

1. **Lowest applicable version** — pick the lowest version satisfying the constraint
2. **Floating versions** (`1.0.*`) — the sole exception, resolving to the **highest** matching version
3. **Direct dependency wins** (nearest wins) — a direct reference overrides transitive requirements, even if this causes a downgrade (triggering **NU1605**, which is a **warning** by default and becomes an error only when `TreatWarningsAsErrors` is enabled)
4. **Cousin dependencies** — when different subgraphs require different versions of the same package, NuGet picks the lowest version satisfying all constraints simultaneously

The practical consequence for titi's monorepo: if LibA depends on `LibB >= 1.0.1` and LibC depends on `LibB >= 1.0.0`, NuGet correctly resolves to LibB **1.0.1**. No conflict. But if a consuming application directly references `LibB 1.0.0` while transitively needing 1.0.1 through LibA, the direct-dependency-wins rule forces **1.0.0**, generating an NU1605 warning. This is the most common "NuGet doesn't care about my patch" scenario in monorepos.

NuGet 6.12 introduced a completely rewritten dependency resolver enabled by default. It has known regressions — particularly **false NU1605 warnings when using CPM transitive pinning** (NuGet/Home#13938). The workaround is `<RestoreUseLegacyDependencyResolver>true</RestoreUseLegacyDependencyResolver>`. Titi should document this and detect which resolver is in use.

The resolver code path is shared across `dotnet restore`, `msbuild /t:Restore`, and Visual Studio since NuGet 4.0+, but SDK version differences between environments can produce different resolution results. Titi's CI validation should enforce a single SDK version via `global.json`.

### How titi's reference swap interacts with resolution

Titi's `ExcludeAssets="All"` trick — keeping a PackageReference in the NuGet graph but suppressing its compiled assets — has a direct effect on resolution. When in ProjectReference mode, NuGet still evaluates the retained PackageReference for transitive resolution purposes but ignores it for compilation. This means:

- Transitive dependencies of the swapped package are still resolved through the NuGet graph, which is the correct behavior.
- If another package in the graph also declares a dependency on the same internal package, NuGet will use the `ExcludeAssets="All"` entry's version as a floor for that resolution. This version must stay in sync with the current source version to avoid a silent mismatch between what NuGet resolves and what ProjectReference actually builds.

After every ProjectReference ↔ PackageReference swap, titi must update the version in the retained PackageReference and regenerate lock files (see the CPM section for the procedure).

---

## Runtime binding diverges from restore-time resolution in critical ways

### The three-way version distinction

There are three distinct version values on a .NET package, each read by a different consumer:

| Version property | Where it lives | Who reads it | Runtime effect |
|---|---|---|---|
| `<Version>` / PackageVersion | NuGet metadata | NuGet resolver at restore time | **None** |
| `<AssemblyVersion>` | Assembly IL metadata | CLR at load time | **Controls binding** |
| `<FileVersion>` | Assembly PE header | Windows Explorer / diagnostics | None |

NuGet resolves **packages** at restore time. The CLR loads **assemblies** at runtime using `AssemblyVersion` alone. These two can disagree, and when they do, runtime failures occur silently.

In .NET Core/5+, the CLR automatically loads an assembly with a version **equal to or higher** than the requested version — no binding redirects needed (unlike .NET Framework). This means a consumer compiled against LibB `AssemblyVersion 1.0.0.0` will successfully load LibB with `AssemblyVersion 1.0.1.0`. However, **a higher version does not guarantee API compatibility**. If LibB 1.0.1 removed or changed a method the consumer calls, the assembly loads successfully but throws `MissingMethodException` or `TypeLoadException` at the call site — a runtime bomb that no restore or build step catches.

### Recommended AssemblyVersion strategy

Microsoft's official guidance for library authors is to set `AssemblyVersion` to `{Major}.0.0.0` only. All ASP.NET Core, EF Core, and Newtonsoft.Json packages follow this pattern. For example, Newtonsoft.Json 13.0.1, 13.0.2, and 13.0.3 all share `AssemblyVersion 13.0.0.0`. This eliminates binding failures across patch and minor updates entirely.

For titi's internal packages, the recommended configuration in `Directory.Build.props` is:

```xml
<PropertyGroup>
  <!-- PackageVersion follows SemVer: changes with every release -->
  <Version>1.2.3</Version>
  <!-- AssemblyVersion: major-only, changes only on breaking changes -->
  <!-- Uses System.Version.Parse to reliably extract the major segment -->
  <AssemblyVersion>$([System.Version]::Parse($(VersionPrefix)).Major).0.0.0</AssemblyVersion>
  <!-- FileVersion: full version for diagnostics -->
  <FileVersion>$(VersionPrefix).0</FileVersion>
</PropertyGroup>
```

> **Important:** `VersionPrefix` must be set explicitly. If you use `<Version>` directly without also setting `<VersionPrefix>`, the `System.Version.Parse` expression will fail. Either set both, or extract the major version in a custom target that runs after version properties are evaluated.

This ensures that within a major version, any consumer can load any patch without binding failures — even if NuGet resolves a slightly different version than what was compiled against.

---

## CPM and lock files provide layered but imperfect protection

Central Package Management via `Directory.Packages.props` enforces a **single version per package** across all projects in the monorepo. Projects use versionless `<PackageReference Include="LibB" />` entries, with the version defined centrally. This directly supports titi's "Single Version of Truth" policy and eliminates the most common diamond-dependency scenario where different projects reference different versions of the same internal package.

However, CPM alone does not control **transitive** dependency versions. Enabling `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` promotes any `<PackageVersion>` entry in `Directory.Packages.props` to act as a floor for transitive resolution too. This is essential for monorepos — without it, a transitive dependency could resolve to a lower version than what CPM specifies.

> **NuGet 6.12 caveat:** The new resolver has a known regression where CPM transitive pinning can produce false NU1605 warnings (NuGet/Home#13938). If you observe spurious downgrade warnings after enabling transitive pinning, add `<RestoreUseLegacyDependencyResolver>true</RestoreUseLegacyDependencyResolver>` to `Directory.Build.props` as a temporary workaround while the NuGet team addresses the regression.

The `packages.lock.json` file adds a second layer of protection. It records the **exact resolved version and content hash** of every direct and transitive dependency per project. With `<RestoreLockedMode>true</RestoreLockedMode>` in CI, restore fails (NU1004) if the lock file doesn't match — preventing dependency drift between local development and CI.

The combination titi should enforce in `Directory.Build.props`:

```xml
<PropertyGroup>
  <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  <!-- Only lock in CI; local dev needs flexibility to run --force-evaluate -->
  <RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
</PropertyGroup>
```

### Lock file regeneration after swaps

Because `RestoreLockedMode` in CI will reject any lock file that doesn't match the current resolution, every ProjectReference ↔ PackageReference swap that changes the effective dependency graph requires a lock file update. The correct procedure is:

1. Perform the swap (or version bump)
2. Temporarily ensure `RestoreLockedMode` is not set (it should only be active in CI)
3. Run `dotnet restore --force-evaluate` to regenerate all lock files
4. Commit the updated lock files alongside the swap
5. CI will then validate against the new lock files

`--force-evaluate` is only meaningful when `RestorePackagesWithLockFile` is set on the project. If a project doesn't have lock files enabled, the flag is silently ignored. Titi's swap command should verify lock file configuration before attempting regeneration and warn if it's missing.

**Key limitation:** Lock files are per-project, not per-solution or per-repo. Two projects referencing the same package can resolve different transitive dependency versions if their direct dependency trees differ. CPM handles version consistency across projects; lock files handle reproducibility over time. Both are required.

---

## Cascading version bumps require graph analysis that no existing tool provides

None of the three major .NET versioning tools — **Nerdbank.GitVersioning (NBGV)**, **MinVer**, or **GitVersion** — detect cascading version bumps. They version projects independently based on git history.

NBGV is best suited for titi's monorepo because it supports per-project `version.json` files with `pathFilters` (only counting commits that touch relevant paths) and `inherit: true` for shared settings. MinVer uses git tags with per-project prefixes (`liba-1.0.0`) but suffers from cross-project commit noise. GitVersion's monorepo support is explicitly acknowledged as weak by its maintainers.

### The cascading bump algorithm

The cascading bump algorithm titi should implement:

1. **Build the dependency graph** by parsing all `.csproj` files for `ProjectReference` edges (in monorepo mode) or `PackageReference` edges to internal packages (in package mode)
2. **Detect changed packages** via `git diff` between HEAD and the last release tag, filtering to source files (`.cs`, `.csproj`, shared `.props`)
3. **Determine bump type** per changed package — from conventional commits, explicit changeset files, or manual specification
4. **Check API surface impact** using `Microsoft.DotNet.ApiCompat` or `PublicApiAnalyzer`. Changes that don't affect the public API surface of a package should **not** propagate upward past the direct dependent. A patch fix deep in the graph should not cause version churn in every library above it.
5. **Topologically sort** the dependency graph (leaves/dependencies first)
6. **Propagate bumps upward** only where the public API surface changed: for each changed package where the API surface was affected, mark all direct dependents for at minimum a patch bump; if the change was major, evaluate whether dependents also need a major bump
7. **Apply the highest bump** when a package has multiple changed dependencies
8. **Update versions** in `version.json` (NBGV), `Directory.Packages.props` (for CPM entries), and regenerate lock files

A changeset-based workflow is more reliable than conventional commits alone. Each PR that modifies library code includes a changeset file specifying which packages changed and what the bump type should be. CI enforces this via a `titi changeset verify` step that rejects PRs touching library code without a changeset. The CI command `titi version detect --dry-run` shows the computed version plan before it is applied.

---

## Metapackages trade consumer simplicity for version rigidity

A NuGet metapackage is a package containing no code — only dependency references. Consumers add one `<PackageReference>` and transitively receive all constituent packages. The minimal `.csproj` for a metapackage:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <!-- Suppress empty assembly warning -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <!-- Suppress NU5128: dependencies without lib assets -->
    <NoWarn>$(NoWarn);NU5128</NoWarn>
    <PackageId>MyCompany.BundleABC</PackageId>
    <Version>1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <!-- PrivateAssets="none" is critical: without it, Build and Analyzer
         assets from constituents do NOT flow to consumers of the bundle.
         For example, a constituent that provides a source generator would
         silently not run for consumers referencing only the bundle. -->
    <PackageReference Include="MyCompany.LibA" Version="2.1.0" PrivateAssets="none" />
    <PackageReference Include="MyCompany.LibB" Version="1.3.0" PrivateAssets="none" />
    <PackageReference Include="MyCompany.LibC" Version="3.0.0" PrivateAssets="none" />
  </ItemGroup>
</Project>
```

### The ASP.NET Core trajectory as a cautionary tale

ASP.NET Core's evolution illustrates the risks of metapackage bundling. In 2.0, `Microsoft.AspNetCore.All` was a pure metapackage pulling in everything including third-party dependencies. By 2.1 it was trimmed to `Microsoft.AspNetCore.App`. **In 3.0, Microsoft abandoned the metapackage pattern entirely** in favor of a shared framework via `<FrameworkReference>`. This shift happened because metapackages created version confusion — users expected NuGet package semantics but got platform-level bundling behavior.

### Real-world bundling patterns in .NET OSS

Large .NET projects use three distinct approaches:

- **Lock-step metapackages** (Orleans): `Microsoft.Orleans.Sdk`, `Microsoft.Orleans.Client`, and `Microsoft.Orleans.Server` are layered metapackages. Historically all Orleans packages shared the same major.minor version, though v9+ has introduced some divergence between packages. Simple to reason about, but version numbers inflate for packages with no changes.
- **Lock-step without metapackages** (Akka.NET): All core packages (Akka, Akka.Remote, Akka.Cluster, etc.) share the exact same version (e.g., 1.5.60) but consumers explicitly reference each one. Documentation states "all Akka modules MUST be of the same version" — enforced by convention, not packaging.
- **Independent versioning, no bundles** (Azure SDK for .NET): 100+ packages each versioned independently (`Azure.Storage.Blobs 12.x`, `Azure.Security.KeyVault.Secrets 4.x`). Consistency enforced through design guidelines and shared build infrastructure, not packaging.

---

## Bundle versioning and CPM interaction

The bundle's version should follow **independent SemVer based on consumer-facing impact**: if any constituent has a breaking change, the bundle gets a major bump; new features get a minor bump; only patches get a patch bump. This is more useful than lock-step versioning because it communicates actual breaking-change risk to consumers.

With CPM, the bundle should appear in `Directory.Packages.props` as a `<PackageVersion>` entry. Individual constituent packages should **also** be listed in CPM for two reasons: some projects may reference individual packages directly, and CPM's transitive pinning needs version entries for constituents to control transitive resolution.

> **CPM transitive pinning + bundle interaction:** When `CentralPackageTransitivePinningEnabled` is true, constituent packages listed in `Directory.Packages.props` get transitively pinned to those CPM versions — which may differ from what the bundle's `.csproj` specifies internally. Ensure constituent versions in `Directory.Packages.props` are **equal to or higher** than the versions the bundle declares for its constituents. A lower CPM floor will silently override the bundle's tested configuration.

### The dual-reference anti-pattern

If a consumer references both `BundleABC` (which depends on `LibA 2.0.0`) and `LibA 3.0.0` directly, NuGet's direct-dependency-wins rule selects LibA 3.0.0, which may be incompatible with LibB and LibC as tested in the bundle. This applies to **transitive** conflicts too: a consumer referencing `BundleABC` and a different package that transitively depends on `LibA 3.0.0` has the same problem, even without any direct reference to LibA. Titi's lint command must walk the full transitive graph — not just direct PackageReferences — to detect these conflicts.

### Constituent removal is a breaking change

If a constituent is removed from a bundle, consumers who relied on it transitively through the bundle will get build breaks on the next bundle update with no warning. Titi should classify constituent removals as **major version bumps** and generate migration warnings in release notes.

### Common failure modes

- **Implicit transitive dependencies:** Consumer code accidentally uses a package brought in by the bundle; when the bundle is updated and that transitive is removed, the consumer breaks with no obvious connection to the bundle update.
- **Stale bundles:** Constituents get security patches but the bundle `.csproj` is not updated and republished.
- **Restore performance degradation:** Large bundles pull in unnecessary packages for consumers who only need one constituent.

### When to use bundles

| Scenario | Recommendation |
|---|---|
| External consumers onboarding for the first time | **Use bundles** — single-reference convenience justifies the trade-offs |
| Internal monorepo coordination between teams | **Use CPM version groups** — avoids transitive bloat and version rigidity |
| Packages with diverging release cadences | **Don't bundle** — lock-step versioning hides independent change history |
| Packages always released and tested together | **Bundle is reasonable** — provided titi enforces the lint rules |

CPM itself can serve as a lightweight alternative to metapackages for the internal coordination case, functioning like Maven's BOM (Bill of Materials) pattern. Rather than forcing all dependencies through a single package, CPM declares version floors centrally and lets projects opt in to only the packages they need.

---

## Concrete titi design extensions

### 6.5.1 Versioning detection strategy — `titi version detect`

```
titi version detect [--dry-run] [--from <tag>] [--apply]
```

This command:

1. Parses the dependency graph from `.csproj` ProjectReference edges
2. Runs `git diff --name-only <last-release>..<HEAD>` to identify changed files
3. Maps changed files to owning projects
4. Reads changeset files (developer-authored, per-PR) to determine bump severity
5. Runs `Microsoft.DotNet.ApiCompat` (or checks `PublicApiAnalyzer` suppressions) to determine whether changes affect the public API surface of each package
6. Walks the dependency graph upward from changed projects, propagating bumps only where the public API surface changed — internal-only changes do not cascade beyond the direct dependent
7. Applies the highest bump type when a package has multiple changed dependencies
8. Outputs a version plan: which projects need bumps, what type, and the new version numbers
9. With `--apply`: updates `version.json` files (NBGV), `Directory.Packages.props` entries, and regenerates `packages.lock.json`

### 6.5.2 Patch-version-safe resolution enforcement — `titi version validate`

```
titi version validate [--fix]
```

Verifies the following invariants across the entire repo:

- `AssemblyVersion` is set to `{Major}.0.0.0` for all internal packages (prevents runtime binding failures)
- CPM is enabled with transitive pinning (`CentralPackageTransitivePinningEnabled`)
- Lock files exist and are up-to-date for all projects with `RestorePackagesWithLockFile`
- `RestoreLockedMode` is enforced in CI
- A single .NET SDK version is pinned via `global.json`
- No project suppresses NU1605 without an explicit justification comment

With `--fix`, automatically applies the `AssemblyVersion` pattern and `global.json` entries where safe to do so.

### 6.5.3 Bundle management — `titi bundle`

Bundle configuration lives in `bundles.yaml` at the repository root:

```yaml
bundles:
  MyCompany.BundleABC:
    project: src/BundleABC/BundleABC.csproj
    versionStrategy: independent   # "independent" or "lockstep"
    constituents:
      - MyCompany.LibA
      - MyCompany.LibB
      - MyCompany.LibC
    deprecated_constituents: []    # packages removed from the bundle; triggers major bump + migration warning
```

**Commands:**

```
titi bundle create <bundle-name> --constituents LibA,LibB,LibC [--strategy independent|lockstep]
```
Scaffolds a metapackage `.csproj` with correct `PrivateAssets="none"`, `IncludeBuildOutput=false`, and `NU5128` suppression. Adds the bundle and all constituents to `Directory.Packages.props`.

```
titi bundle check <bundle-name>
```
Compares constituent versions referenced in the bundle's `.csproj` against the current versions in `Directory.Packages.props`. Reports drift.

```
titi bundle update <bundle-name> [--dry-run] [--strategy independent|lockstep]
```
Computes the new bundle version based on the highest SemVer impact across constituent changes. Updates the bundle's `.csproj` and `Directory.Packages.props`. Classifies any constituent removal as a breaking (major) change.

```
titi bundle lint [--all]
```
Scans all `.csproj` files and `Directory.Packages.props` for:
- Consumers referencing both a bundle and individual constituents (direct or transitive)
- Version conflicts between bundle-declared constituent versions and CPM transitive pins
- Stale bundles (constituents have newer published versions than the bundle references)
- Constituent removals not declared in `deprecated_constituents`

Outputs actionable warnings with specific project paths and version numbers.

---

## Conclusion

The core insight is that NuGet's resolver and the CLR's loader operate on fundamentally different version concepts — package versions vs. assembly versions — and the failure modes at each layer are distinct. **Patch version "invisibility" at resolve time is a feature, not a bug**, but it requires deliberate compensating strategies: major-only `AssemblyVersion` (prevents runtime binding failures), CPM with transitive pinning (prevents version splits), and graph-based cascading bump detection gated on API surface changes (prevents version churn without meaningful impact).

For bundles, CPM-based version groups are safer than metapackages for internal monorepo coordination, while metapackages remain valuable for external consumers who want single-reference convenience — provided titi enforces lint rules against the transitive dual-reference anti-pattern, classifies constituent removals as breaking changes, and automates bundle version propagation.
