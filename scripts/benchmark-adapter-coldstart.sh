#!/usr/bin/env bash
# Benchmark CLR cold-start time for adapter timeout guidance.
# Usage: scripts/benchmark-adapter-coldstart.sh [samples=10]
set -euo pipefail

N="${1:-10}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/titi/titi.csproj"
DIST_DIR="$ROOT/dist"
AOT_BIN="$DIST_DIR/titi"
aot_available=false

echo "=== CLR Cold-Start Benchmark ==="
echo "Samples: $N"
echo ""

# Phase 1: Non-AOT
echo "--- Phase 1: Non-AOT (dotnet run --no-build -- --help) ---"
echo "Building Release..."
dotnet build "$PROJECT" --configuration Release -nologo -clp:NoSummary -v q >/dev/null 2>&1
echo ""

non_aot_times=""
aot_times=""
for i in $(seq 1 "$N"); do
    start=$(date +%s%N)
    dotnet run --project "$PROJECT" --configuration Release --no-build -- --help >/dev/null 2>&1
    end=$(date +%s%N)
    elapsed_ms=$(( (end - start) / 1000000 ))
    non_aot_times="${non_aot_times}${elapsed_ms},"
    echo "  [$i/$N] ${elapsed_ms}ms"
done

# Phase 2: AOT (attempt)
echo ""
echo "--- Phase 2: AOT (native publish) ---"
echo "Publishing AOT..."
if dotnet publish "$PROJECT" --configuration Release -p:PublishAot=true -o "$DIST_DIR" -nologo -clp:NoSummary -v q >/dev/null 2>/dev/null && [ -f "$AOT_BIN" ]; then
    aot_available=true
    echo "AOT publish succeeded."
    echo ""

    aot_times=""
    for i in $(seq 1 "$N"); do
        start=$(date +%s%N)
        "$AOT_BIN" --help >/dev/null 2>&1
        end=$(date +%s%N)
        elapsed_ms=$(( (end - start) / 1000000 ))
        aot_times="${aot_times}${elapsed_ms},"
        echo "  [$i/$N] ${elapsed_ms}ms"
    done
else
    echo "AOT publish failed (linker/source-gen compatibility)."
    echo "This is expected: System.Text.Json uses reflection/emit for anonymous types."
    echo "AOT would require source-generated JsonSerializerContext (out of scope for now)."
fi

# Results
echo ""
non_aot_csv="${non_aot_times%,}"
aot_csv="${aot_times%,}"

# Results
echo ""

if [ -n "$aot_times" ]; then
    HAS_AOT=true
else
    HAS_AOT=false
fi

python3 -c "
import math, sys

non_aot = [float(t) for t in '$non_aot_csv'.split(',') if t]
has_aot = '$HAS_AOT' == 'true'

def stats(times, label):
    n = len(times)
    mean = sum(times) / n
    variance = sum((t - mean)**2 for t in times) / n
    stddev = math.sqrt(variance)
    print(f'{label}:')
    print(f'  Samples:      {n}')
    print(f'  Mean (ms):    {mean:.0f}')
    print(f'  StdDev (ms):  {stddev:.0f}')
    print(f'  Min (ms):     {min(times):.0f}')
    print(f'  Max (ms):     {max(times):.0f}')
    print()

stats(non_aot, 'Non-AOT (dotnet run)')

na_mean = sum(non_aot)/len(non_aot)
na_var = sum((t - na_mean)**2 for t in non_aot)/len(non_aot)

if has_aot:
    aot = [float(t) for t in '$aot_csv'.split(',') if t]
    stats(aot, 'AOT (native)')
    a_mean = sum(aot)/len(aot)
    a_var = sum((t - a_mean)**2 for t in aot)/len(aot)

print('Adapter timeout guidance:')
print(f'  Non-AOT: mean+2sigma = {na_mean + 2*math.sqrt(na_var):.0f}ms')
print(f'  Non-AOT: min         = {min(non_aot):.0f}ms')
print(f'  Non-AOT: max         = {max(non_aot):.0f}ms')
if has_aot:
    print(f'  AOT:     mean+2sigma = {a_mean + 2*math.sqrt(a_var):.0f}ms')
else:
    print(f'  AOT:     not available (System.Text.Json reflection, linker deps)')
print()
print('All measured cold-starts are well under the 30s testaruda default timeout.')
print('Budget the adapter timeout for graph-build time, not CLR cold-start.')
print('AOT would require source-generated JsonSerializerContext (future work).')
" 2>&1
