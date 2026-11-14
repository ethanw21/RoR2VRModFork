#!/usr/bin/env bash
# Assembles the Thunderstore-layout package in dist/package/: the published package that
# fetch-refs.sh unpacked, with the freshly built DLLs and this repository's manifest, README,
# changelog and icon put in place of the published ones. Run after `dotnet build -c Release`.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BASE="$ROOT/refs/package"
OUT="$ROOT/dist/package"

[ -d "$BASE" ] || { echo "error: refs/package is missing; run build-tools/ci/fetch-refs.sh first"; exit 1; }

rm -rf "$ROOT/dist"
mkdir -p "$ROOT/dist"
cp -r "$BASE" "$OUT"

replace() { # dll name, project folder, required (1/0)
    local name="$1" built
    built="$(find "$ROOT/$2/bin/Release" -type f -name "$name" | head -n 1 || true)"
    [ -n "$built" ] || { echo "error: $name was not built"; exit 1; }
    local hits
    hits="$(find "$OUT" -type f -name "$name")"
    if [ -z "$hits" ]; then
        if [ "$3" = "1" ]; then echo "error: the published package has no $name to replace"; exit 1; fi
        echo "  $name: built, but not part of the published package - left out of the package"
        mkdir -p "$ROOT/dist/extra" && cp "$built" "$ROOT/dist/extra/$name"
        return
    fi
    while IFS= read -r target; do
        cp "$built" "$target"
        echo "  $name  ->  ${target#$OUT/}"
    done <<< "$hits"
}

echo "== Built DLLs"
replace VRMod.dll RoR2VRMod 1
replace VRPatcher.dll VREnabler 0
replace VRAPI.dll VRAPI 0

echo "== Package files from the repository"
for f in manifest.json README.md CHANGELOG.md icon.png; do
    if [ -f "$ROOT/$f" ]; then cp "$ROOT/$f" "$OUT/$f"; echo "  $f"; fi
done

echo "== dist/package"
(cd "$OUT" && find . -type f -printf '  %10s  %p\n' | sort -k2)
(cd "$OUT" && find . -type f -name '*.dll' -print0 | sort -z | xargs -0 sha256sum)
