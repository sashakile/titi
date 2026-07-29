#!/usr/bin/env bash
# Generate docs/specs/toc.yml from openspec/specs/ directory structure
set -euo pipefail

SPECS_DIR="$1"
OUTPUT="$SPECS_DIR/toc.yml"

echo "items:" > "$OUTPUT"
for dir in "$SPECS_DIR"/*/; do
  name="$(basename "$dir")"
  echo "  - name: $name" >> "$OUTPUT"
  echo "    href: $name/spec.md" >> "$OUTPUT"
done
