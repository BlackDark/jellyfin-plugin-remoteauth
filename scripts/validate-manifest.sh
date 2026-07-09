#!/usr/bin/env bash
set -euo pipefail

MANIFEST="${1:-manifest.json}"

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required" >&2
  exit 1
fi

entries=$(jq -r '.[0].versions[] | "\(.version)|\(.sourceUrl)|\(.checksum)"' "$MANIFEST")
if [ -z "$entries" ]; then
  echo "No versions found in ${MANIFEST}" >&2
  exit 1
fi

while IFS='|' read -r version source_url checksum; do
  echo "Checking ${version}: ${source_url}"
  actual_checksum=$(curl -fsSL "$source_url" | md5sum | cut -d' ' -f1)
  if [ "$actual_checksum" != "$checksum" ]; then
    echo "Checksum mismatch for ${version}: expected ${checksum}, got ${actual_checksum}" >&2
    exit 1
  fi
done <<< "$entries"

echo "All manifest source URLs are reachable and checksums match."
