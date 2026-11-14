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

TOOL="$ROOT/refs/build/_tools/ExtractResources/ExtractResources.dll"

compare() { # dll name, built file: what the published DLL and the new one reference and embed
    local published
    published="$(find "$BASE" -type f -name "$1" | head -n 1 || true)"
    [ -n "$published" ] && [ -f "$TOOL" ] || return 0
    if diff <(dotnet "$TOOL" --info "$published") <(dotnet "$TOOL" --info "$2") > "$ROOT/dist/$1.diff"; then
        echo "  $1: references and embedded files match the published DLL"
    else
        echo "  $1: references or embedded files DIFFER from the published DLL ('<' published, '>' this build):"
        sed 's/^/      /' "$ROOT/dist/$1.diff"
        echo "::warning title=$1 differs from the published build::Its referenced assemblies or embedded files changed. See the Package step's log."
    fi
    rm -f "$ROOT/dist/$1.diff"
}

replace() { # dll name, project folder, required (1/0)
    local name="$1" built
    built="$(find "$ROOT/$2/bin/Release" -type f -name "$name" | head -n 1 || true)"
    [ -n "$built" ] || { echo "error: $name was not built"; exit 1; }
    compare "$name" "$built"
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
