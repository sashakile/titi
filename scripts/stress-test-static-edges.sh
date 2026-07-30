#!/usr/bin/env bash
# Stress-test static edge analysis against 10 .NET repos.
# Validates >0 static edges for at least 7/10 repos.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TITI_BIN="${TITI_BIN:-$(which titi)}"
WORK_DIR="${WORK_DIR:-/tmp/titi-stress-test}"
RESULTS_FILE="$WORK_DIR/results.md"
PASS=0
FAIL=0
TOTAL=10

# Repos: well-known .NET open-source projects with test projects
# Format: "org/repo  source-root"
declare -a REPOS=(
  "serilog/serilog  src"
  "fluentvalidation/FluentValidation  src"
  "Humanizr/Humanizer  src"
  "jbogard/ContosoUniversity  src"
  "dotnet/format  src"
  "NLog/NLog  src"
  "ncalc/ncalc  src"
  "morelinq/MoreLinq  MoreLinq"
  "nemesv/IndexRange  src"
  "dotnet/command-line-api  src"
)

mkdir -p "$WORK_DIR"
cd "$WORK_DIR"

echo "# Static Edge Stress Test Results" > "$RESULTS_FILE"
echo "Date: $(date -u +%Y-%m-%dT%H:%M:%SZ)" >> "$RESULTS_FILE"
echo "titi version: $($TITI_BIN --help 2>&1 | head -1)" >> "$RESULTS_FILE"
echo "" >> "$RESULTS_FILE"
echo "| # | Repo | Source Roots | Projects | Test Projects | Static Edges | Result |" >> "$RESULTS_FILE"
echo "|---|---|---|---|---|---|---|" >> "$RESULTS_FILE"

echo "=== Static Edge Stress Test ==="
echo "Work dir: $WORK_DIR"
echo "Titi bin: $TITI_BIN"
echo ""

for i in "${!REPOS[@]}"; do
  entry="${REPOS[$i]}"
  repo="${entry%% *}"
  source_root="${entry#* }"
  num=$((i + 1))
  repo_dir_name=$(echo "$repo" | tr '/' '_')
  repo_dir="$WORK_DIR/$repo_dir_name"

  echo ""
  echo "--- [$num/$TOTAL] $repo (source-root: $source_root) ---"

  # Clone or pull
  if [ -d "$repo_dir" ]; then
    echo "  Pulling existing clone..."
    cd "$repo_dir" && git pull --ff-only 2>/dev/null || true
    cd "$WORK_DIR"
  else
    echo "  Cloning..."
    git clone --depth 1 "https://github.com/$repo.git" "$repo_dir" 2>/dev/null || {
      echo "  ⚠ Clone failed — skipping"
      echo "| $num | $repo | $source_root | clone-failed | - | 0 | ⚠ SKIP |" >> "$RESULTS_FILE"
      continue
    }
  fi

  cd "$repo_dir"

  # Create titi.config.json if not present
  if [ ! -f "titi.config.json" ]; then
    # Check which source directories actually exist
    IFS=',' read -ra roots <<< "$source_root"
    existing_roots=()
    for r in "${roots[@]}"; do
      r_trimmed=$(echo "$r" | xargs)
      if [ -d "$r_trimmed" ]; then
        existing_roots+=("\"$r_trimmed\"")
      fi
    done

    if [ ${#existing_roots[@]} -eq 0 ]; then
      # Try to find .csproj files
      csproj_dirs=$(find . -name "*.csproj" -not -path "./.*" -not -path "*/obj/*" -not -path "*/bin/*" 2>/dev/null | head -5 | xargs -I{} dirname {} | sort -u | head -5)
      if [ -n "$csproj_dirs" ]; then
        echo "  Found csproj dirs: $csproj_dirs"
        # Use the common parent
        # For simplicity, just use "."
        existing_roots=("\".\"")
      else
        echo "  ⚠ No .csproj files found — skipping"
        echo "| $num | $repo | $source_root | no-csproj | - | 0 | ⚠ SKIP |" >> "$RESULTS_FILE"
        cd "$WORK_DIR"
        continue
      fi
    fi

    roots_json=$(IFS=,; echo "${existing_roots[*]}")
    cat > "titi.config.json" << CFGEOF
{
  "source-roots": [$roots_json],
  "test-detection-enabled": true,
  "fallback-threshold": 0.0
}
CFGEOF
    echo "  Created titi.config.json with source-roots: [$roots_json]"
  fi

  # Create a git baseline for diff detection
  git config user.email "stress-test@titi.local" 2>/dev/null || true
  git config user.name "Stress Test" 2>/dev/null || true

  # Commit everything first to create a clean baseline
  git add -A 2>/dev/null || true
  git commit -m "baseline for stress test" 2>/dev/null || true

  # Create a dummy commit to ensure we have a diff to analyze
  touch .stress-test-marker
  git add .stress-test-marker
  git commit -m "stress test trigger" 2>/dev/null || true

  # Run titi test-manifest --select to trigger static analysis
  echo "  Running titi test-manifest --select..."
  set +e
  output=$($TITI_BIN test-manifest --select 2>&1)
  exit_code=$?
  set -e

  echo "$output" | head -20

  # Clean up the dummy commit
  git reset --soft HEAD~1 2>/dev/null || true
  git reset HEAD .stress-test-marker 2>/dev/null || true
  rm -f .stress-test-marker
  git commit -m "restore after stress test" 2>/dev/null || true

  # Count projects
  proj_count=$(echo "$output" | grep -c "project" || true)
  test_proj_count=$(echo "$output" | grep -c "test" || true)

  # Check for static edges
  edges_found=0
  if echo "$output" | grep -q "Computed .* static edge"; then
    edges_found=$(echo "$output" | grep -oP 'Computed \K(\d+)' || echo "0")
  fi

  # Also check the cached file
  static_cache="$repo_dir/.titi/test-cache/edges/static-edges.json"
  if [ -f "$static_cache" ]; then
    cached_count=$(python3 -c "
import json
with open('$static_cache') as f:
    data = json.load(f)
print(len(data))
" 2>/dev/null || echo "parse-error")
    echo "  Cached static edges: $cached_count"
    if [ "$cached_count" != "parse-error" ] && [ "$cached_count" -gt 0 ] 2>/dev/null; then
      edges_found=$cached_count
    fi
  fi

  if [ "$edges_found" -gt 0 ] 2>/dev/null; then
    echo "  ✅ PASS: $edges_found static edges"
    PASS=$((PASS + 1))
    result="✅ PASS"
  else
    echo "  ❌ FAIL: 0 static edges"
    FAIL=$((FAIL + 1))
    result="❌ FAIL"
  fi

  echo "| $num | $repo | $source_root | $proj_count | $test_proj_count | $edges_found | $result |" >> "$RESULTS_FILE"

  # Cleanup repo to save space
  cd "$WORK_DIR"

  echo "  Done."
done

# Summary
echo ""
echo "=== Results ==="
echo ""
echo "| # | Repo | Source Roots | Projects | Test Projects | Static Edges | Result |"
echo "|---|---|---|---|---|---|---|"
cat "$RESULTS_FILE" | grep -v "^#" | grep -v "^$" | grep -v "|---|---|"

echo ""
echo "---"
echo "Passed: $PASS/$TOTAL"
echo "Failed: $FAIL/$TOTAL"
echo ""

if [ "$PASS" -ge 7 ]; then
  echo "✅ STRESS TEST PASSED: $PASS/$TOTAL repos have >0 static edges"
  exit 0
else
  echo "❌ STRESS TEST FAILED: Only $PASS/$TOTAL repos have >0 static edges (need ≥7)"
  exit 1
fi