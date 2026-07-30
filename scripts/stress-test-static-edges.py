#!/usr/bin/env python3
"""Stress-test static edge analysis against 10 .NET repos.

Validates that >0 static edges (method-call or using-statement level)
are produced for at least 7/10 repos, using the same regex-based approach
as titi's StaticEdgeAnalyzer.
"""

import os
import re
import sys
import json
import subprocess
import tempfile
import shutil
from pathlib import Path
from datetime import datetime, timezone

# ── Regex patterns (mirroring StaticEdgeAnalyzer) ────────────────
# Method declarations: `public [static] ReturnType MethodName(`
METHOD_DECL_RE = re.compile(
    r'\b(?:public|internal|private|protected)\s+'
    r'(?:static\s+)?'
    r'(?:override\s+|virtual\s+|abstract\s+)?'
    r'[\w\?\[\],<>~`]+\s+'
    r'(\w+)\s*\('
)

# Class/record/struct declarations
TYPE_DECL_RE = re.compile(
    r'\b(?:public|internal|private|protected)?\s*'
    r'(?:static\s+)?'
    r'(?:class|record|struct)\s+(\w+)'
)

# Method calls in test code: `TypeName.MethodName(`
METHOD_CALL_RE = re.compile(r'\b([A-Z]\w*)\.(\w+)\s*\(')

# Constructor calls: `new TypeName(`
NEW_TYPE_RE = re.compile(r'\bnew\s+([A-Z]\w*)\s*\(')

# Using directives
USING_RE = re.compile(r'^\s*using\s+(?:static\s+)?([^;]+?)(?:\s*;)\s*$', re.MULTILINE)

# Namespace declarations
NAMESPACE_RE = re.compile(r'^\s*namespace\s+([^\s{;]+)(?:\s*[\{;])?\s*$', re.MULTILINE)


# ── Repos ────────────────────────────────────────────────────────
REPOS = [
    ("serilog/serilog", ""),
    ("fluentvalidation/FluentValidation", ""),
    ("Humanizr/Humanizer", ""),
    ("dotnet/roslyn-analyzers", ""),
    ("davidfowl/AspNetCoreDiagnosticScenarios", ""),
    ("NLog/NLog", ""),
    ("ncalc/ncalc", ""),
    ("morelinq/MoreLinq", ""),
    ("AutoMapper/AutoMapper", ""),
    ("dotnet/command-line-api", ""),
]

WORK_DIR = Path("/tmp/titi-stress-test")
CACHE_DIR = WORK_DIR / "repos"


def enumerate_source_files(project_dir: Path) -> list[Path]:
    """Enumerate .cs files excluding obj/ and bin/."""
    files = []
    for f in project_dir.rglob("*.cs"):
        rel = f.relative_to(project_dir)
        parts = rel.parts
        if any(p in ("obj", "bin") for p in parts):
            continue
        files.append(f)
    return sorted(files)


def parse_method_declarations(files: list[Path]) -> dict[str, set[Path]]:
    """Build method name → set of source files map."""
    method_map: dict[str, set[Path]] = {}
    for f in files:
        try:
            content = f.read_text(encoding="utf-8", errors="replace")
            for m in METHOD_DECL_RE.finditer(content):
                name = m.group(1)
                if name not in method_map:
                    method_map[name] = set()
                method_map[name].add(f)
        except Exception:
            pass
    return method_map


def parse_type_declarations(files: list[Path]) -> dict[str, set[Path]]:
    """Build type name → set of source files map."""
    type_map: dict[str, set[Path]] = {}
    for f in files:
        try:
            content = f.read_text(encoding="utf-8", errors="replace")
            for m in TYPE_DECL_RE.finditer(content):
                name = m.group(1)
                if name not in type_map:
                    type_map[name] = set()
                type_map[name].add(f)
        except Exception:
            pass
    return type_map


def parse_namespaces(files: list[Path]) -> dict[str, set[Path]]:
    """Build namespace → set of source files map."""
    ns_map: dict[str, set[Path]] = {}
    for f in files:
        try:
            content = f.read_text(encoding="utf-8", errors="replace")
            m = NAMESPACE_RE.search(content)
            if m:
                ns = m.group(1).strip()
                if ns not in ns_map:
                    ns_map[ns] = set()
                ns_map[ns].add(f)
        except Exception:
            pass
    return ns_map


def parse_using_directives(files: list[Path]) -> dict[Path, set[str]]:
    """Build file → set of using directives map (filtering system/third-party)."""
    filtered_prefixes = ("System", "Microsoft", "Xunit", "NUnit", "MSTest",
                         "NUnit3TestAdapter", "Coverlet")
    using_map: dict[Path, set[str]] = {}
    for f in files:
        try:
            content = f.read_text(encoding="utf-8", errors="replace")
            usings = set()
            for m in USING_RE.finditer(content):
                full = m.group(1).strip().rstrip(".")
                if "=" in full:
                    continue  # alias
                if full.startswith(filtered_prefixes):
                    continue
                usings.add(full)
            if usings:
                using_map[f] = usings
        except Exception:
            pass
    return using_map


def parse_method_calls(files: list[Path],
                       method_map: dict[str, set[Path]],
                       type_map: dict[str, set[Path]]) -> dict[Path, set[Path]]:
    """Build file → set of matched source file paths based on method calls."""
    call_map: dict[Path, set[Path]] = {}
    for f in files:
        try:
            content = f.read_text(encoding="utf-8", errors="replace")
            matched = set()

            for m in METHOD_CALL_RE.finditer(content):
                type_name = m.group(1)
                method_name = m.group(2)

                if method_name in method_map:
                    matched.update(method_map[method_name])
                if type_name in type_map:
                    matched.update(type_map[type_name])

            for m in NEW_TYPE_RE.finditer(content):
                type_name = m.group(1)
                if type_name in type_map:
                    matched.update(type_map[type_name])

            if matched:
                call_map[f] = matched
        except Exception:
            pass
    return call_map


def is_test_project(csproj_path: Path) -> bool:
    """Check if a .csproj is a test project."""
    try:
        content = csproj_path.read_text(encoding="utf-8", errors="replace")

        # Explicit flag
        if '<IsTestProject>true</IsTestProject>' in content:
            return True

        # Check for test SDK references in csproj
        test_sdks = [
            "xunit", "NUnit", "MSTest.TestAdapter",
            "Microsoft.NET.Test.Sdk", "nunit.framework",
            "Microsoft.VisualStudio.TestPlatform",
            "FluentAssertions", "Shouldly",
        ]
        for sdk in test_sdks:
            if f'Include="{sdk}"' in content:
                return True

        # Also check Directory.Build.props in parent dirs for test SDK refs
        parent = csproj_path.parent
        for _ in range(3):  # Check up to 3 levels up
            dbp = parent / "Directory.Build.props"
            if dbp.exists():
                try:
                    dbp_content = dbp.read_text(encoding="utf-8", errors="replace")
                    for sdk in test_sdks:
                        if f'Include="{sdk}"' in dbp_content:
                            return True
                except Exception:
                    pass
            parent = parent.parent

        # Heuristic: project name containing .Tests or ending in Tests
        name = csproj_path.stem  # e.g., System.CommandLine.Tests
        if name.endswith("Tests") or ".Tests." in name:
            return True

        return False
    except Exception:
        return False


def scan_projects(repo_dir: Path) -> tuple[list[Path], list[Path]]:
    """Scan entire repo for .csproj files, separate into test and source."""
    all_csproj = list(repo_dir.rglob("*.csproj"))
    all_csproj = [c for c in all_csproj
                  if not any(p in c.parts for p in ("obj", "bin"))]

    test_projects = []
    source_projects = []
    for c in all_csproj:
        if is_test_project(c):
            test_projects.append(c)
        else:
            source_projects.append(c)

    return test_projects, source_projects


def analyze_repo(repo_dir: Path, source_root: str) -> dict:
    """Run static edge analysis on a repo. Returns result dict."""
    result = {
        "repo": str(repo_dir),
        "source_root": source_root,
        "projects": 0,
        "test_projects": 0,
        "source_files": 0,
        "test_files": 0,
        "l3_method_calls": 0,
        "l2_using_matches": 0,
        "total_static_edges": 0,
        "status": "unknown",
        "error": None,
    }

    # Scan entire repo for .csproj files
    test_projects, source_projects = scan_projects(repo_dir)

    result["projects"] = len(test_projects) + len(source_projects)
    result["test_projects"] = len(test_projects)
    result["source_projects"] = len(source_projects)

    if not test_projects or not source_projects:
        result["status"] = "no-test-or-source"
        result["error"] = f"test_projects={len(test_projects)}, source_projects={len(source_projects)}"
        return result

    # Get source files from source projects
    source_files = []
    for sp in source_projects:
        proj_dir = sp.parent
        source_files.extend(enumerate_source_files(proj_dir))
    source_files = list(set(source_files))

    # Get test files from test projects
    test_files = []
    for tp in test_projects:
        proj_dir = tp.parent
        test_files.extend(enumerate_source_files(proj_dir))
    test_files = list(set(test_files))

    result["source_files"] = len(source_files)
    result["test_files"] = len(test_files)

    if not source_files or not test_files:
        result["status"] = "no-source-or-test-files"
        result["error"] = f"source_files={len(source_files)}, test_files={len(test_files)}"
        return result

    # Build method and type maps from source files
    method_map = parse_method_declarations(source_files)
    type_map = parse_type_declarations(source_files)
    ns_map = parse_namespaces(source_files)

    # Level 3: Method call analysis
    call_map = parse_method_calls(test_files, method_map, type_map)
    l3_edges = sum(len(v) for v in call_map.values())
    result["l3_method_calls"] = l3_edges

    # Level 2: Using statement analysis
    using_map = parse_using_directives(test_files)
    l2_edges = 0
    for test_file, usings in using_map.items():
        for using_ns in usings:
            if using_ns in ns_map:
                l2_edges += len(ns_map[using_ns])
    result["l2_using_matches"] = l2_edges

    # Total: L3 edges (most precise)
    result["total_static_edges"] = l3_edges

    # Status
    if l3_edges > 0:
        result["status"] = "PASS"
    elif l2_edges > 0:
        result["status"] = "L2_ONLY"
    else:
        result["status"] = "FAIL"
        result["error"] = (
            f"No static edges: L3={l3_edges}, L2={l2_edges}, "
            f"methods={len(method_map)}, types={len(type_map)}, ns={len(ns_map)}"
        )

    return result


def clone_repo(repo_slug: str, target_dir: Path) -> bool:
    """Clone a GitHub repo. Returns True if successful."""
    if target_dir.exists():
        print(f"    Pulling existing clone...")
        try:
            subprocess.run(["git", "pull", "--ff-only"],
                           cwd=target_dir, capture_output=True, timeout=120)
            return True
        except Exception:
            # Try fresh clone
            shutil.rmtree(target_dir)

    print(f"    Cloning {repo_slug}...")
    url = f"https://github.com/{repo_slug}.git"
    try:
        subprocess.run(
            ["git", "clone", "--depth", "1", url, str(target_dir)],
            capture_output=True, timeout=300, check=True
        )
        return True
    except subprocess.CalledProcessError as e:
        print(f"    ⚠ Clone failed: {e.stderr.decode()[:200]}")
        return False
    except Exception as e:
        print(f"    ⚠ Clone failed: {e}")
        return False


def main():
    print("=== Static Edge Stress Test ===")
    print(f"Work dir: {WORK_DIR}")
    print(f"Time: {datetime.now(timezone.utc).isoformat()}")
    print()

    WORK_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_DIR.mkdir(parents=True, exist_ok=True)

    results = []
    passed = 0
    failed = 0
    skipped = 0

    for i, (repo, source_root) in enumerate(REPOS, 1):
        repo_name = repo.replace("/", "_")
        repo_dir = CACHE_DIR / repo_name

        print(f"--- [{i}/{len(REPOS)}] {repo} ---")

        ok = clone_repo(repo, repo_dir)
        if not ok:
            skipped += 1
            results.append({
                "repo": repo,
                "status": "SKIP",
                "total_static_edges": 0,
                "projects": 0,
                "test_projects": 0,
                "source_files": 0,
                "test_files": 0,
                "l3_method_calls": 0,
                "l2_using_matches": 0,
                "error": "clone failed",
            })
            continue

        r = analyze_repo(repo_dir, source_root)
        r["repo"] = repo

        status = r["status"]
        edges = r["total_static_edges"]
        l2 = r["l2_using_matches"]
        l3 = r["l3_method_calls"]

        if status == "PASS":
            passed += 1
            print(f"    ✅ PASS: {edges} L3 edges + {l2} L2 edges")
        elif status == "L2_ONLY":
            passed += 1
            print(f"    ⚠ L2_ONLY: {l2} L2 edges (no L3 method calls)")
        else:
            failed += 1
            print(f"    ❌ FAIL: {r.get('error', 'no edges')}")

        results.append(r)

    # Summary
    print()
    print("=== Results ===")
    print()
    header = "| # | Repo | Projects | Test Proj | Source Files | Test Files | L3 Edges | L2 Edges | Total | Result |"
    sep = "|---" * 10 + "|"
    print(header)
    print(sep)

    for i, r in enumerate(results, 1):
        status_icon = {"PASS": "✅", "L2_ONLY": "⚠️", "FAIL": "❌", "SKIP": "⏭️"}.get(r["status"], "❓")
        print(
            f"| {i} | {r['repo']} | {r['projects']} | {r['test_projects']} "
            f"| {r['source_files']} | {r['test_files']} "
            f"| {r['l3_method_calls']} | {r['l2_using_matches']} "
            f"| {r['total_static_edges']} | {status_icon} |"
        )

    print()
    total_valid = len(REPOS) - skipped
    print(f"Passed: {passed}/{total_valid}")
    print(f"Failed: {failed}/{total_valid}")
    print(f"Skipped: {skipped}/{len(REPOS)}")
    print()

    if passed >= 7 and total_valid >= 10:
        print(f"✅ STRESS TEST PASSED: {passed}/{total_valid} repos have >0 static edges")
        return 0
    elif passed >= 7:
        print(f"✅ STRESS TEST PASSED (with skips): {passed}/{total_valid} valid repos have >0 static edges")
        return 0
    else:
        print(f"❌ STRESS TEST FAILED: Only {passed}/{total_valid} repos have >0 static edges (need ≥7)")
        return 1


if __name__ == "__main__":
    sys.exit(main())