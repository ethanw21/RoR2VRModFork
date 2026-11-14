#!/usr/bin/env bash
# Fills refs/lib/ with every library a build needs that is neither in this repository nor on
# NuGet, and unpacks the published mod package into refs/package/ (the packaging step reuses it).
#
#   1. The published Thunderstore package of this mod: the XR / input libraries it ships are
#      both compiled against and shipped again unchanged.
#   2. The bHaptics and Shockwave SDK libraries, compile-time only (from that package, or taken
#      out of DrBibop's original VRPatcher.dll, which carries them as embedded files).
#   3. Reference-only builds of two Unity packages the GAME ships (post-processing and the
#      tracked pose driver), compiled from Unity's public package registry.
#   4. A reference-only stub of the game's Decalicious.dll.
#
# Nothing here touches a copy of the game. Requires: bash, curl, unzip, tar, python3, dotnet SDK.
# Everything it writes is under refs/, which git ignores. Safe to run again.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CI="$ROOT/build-tools/ci"
REFS="$ROOT/refs"
LIB="$REFS/lib"
DL="$REFS/dl"

# Published package this build is based on. Override with environment variables if the mod is
# published under another name; TS_VERSION pins a version instead of taking the latest.
TS_NAMESPACE="${TS_NAMESPACE:-Blowntobytes}"
TS_NAME="${TS_NAME:-Resurrected_VRMod}"
TS_VERSION="${TS_VERSION:-}"

# Game-side Unity packages compiled for reference only.
POSTPROCESSING_VERSION="${POSTPROCESSING_VERSION:-3.2.2}"
LEGACY_INPUT_HELPERS_VERSION="${LEGACY_INPUT_HELPERS_VERSION:-2.1.13}"

rm -rf "$LIB" "$REFS/package" "$REFS/original" "$REFS/build"
mkdir -p "$LIB" "$DL"

fetch() { # url, output file
    echo "  download $1"
    curl -fsSL --retry 4 --retry-delay 3 -A "ror2vrmod-build" -o "$2" "$1"
}

thunderstore() { # namespace, name, version ('' = latest), output dir
    local ns="$1" name="$2" version="$3" out="$4" url=""
    if [ -z "$version" ]; then
        url="$(curl -fsSL --retry 4 --retry-delay 3 -A "ror2vrmod-build" \
            "https://thunderstore.io/api/experimental/package/$ns/$name/" \
            | python3 -c 'import sys, json; print(json.load(sys.stdin)["latest"]["download_url"])' || true)"
        if [ -z "$url" ]; then
            version="$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1]))["version_number"])' "$ROOT/manifest.json")"
            echo "  could not ask Thunderstore for the latest $ns/$name; trying version $version from manifest.json"
        fi
    fi
    [ -n "$url" ] || url="https://thunderstore.io/package/download/$ns/$name/$version/"
    fetch "$url" "$DL/$ns-$name.zip"
    mkdir -p "$out"
    unzip -q -o "$DL/$ns-$name.zip" -d "$out"
}

from_packages() { # file name -> copies the first match into refs/lib, returns 1 if absent
    local hit
    hit="$(find "$REFS/package" "$REFS/original" -type f -iname "$1" 2>/dev/null | head -n 1 || true)"
    [ -n "$hit" ] || return 1
    cp "$hit" "$LIB/$1"
    echo "  $1  <-  ${hit#$REFS/}"
}

ref_assembly() { # assembly name, directory holding the sources
    local asm="$1" src="$2" proj="$REFS/build/$1"
    mkdir -p "$proj"
    # Copy the .cs files, leaving out editor / test code and nested assemblies.
    python3 - "$src" "$proj/src" <<'PY'
import os, shutil, sys
src, dst = sys.argv[1], sys.argv[2]
count = 0
for dirpath, dirnames, filenames in os.walk(src):
    keep = []
    for d in dirnames:
        full = os.path.join(dirpath, d)
        if d.lower() in ("editor", "tests", "test", "samples", "samples~", "documentation~") or d.endswith("~"):
            continue
        if any(f.endswith(".asmdef") for f in os.listdir(full)):
            continue
        keep.append(d)
    dirnames[:] = keep
    for f in filenames:
        if f.endswith(".cs"):
            target = os.path.join(dst, os.path.relpath(os.path.join(dirpath, f), src))
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copyfile(os.path.join(dirpath, f), target)
            count += 1
print(f"  {count} source files")
if count == 0:
    sys.exit("no source files found in " + src)
PY
    cp "$CI/RefAssembly.csproj.template" "$proj/$asm.csproj"
    dotnet build "$proj/$asm.csproj" -c Release --nologo -v quiet -o "$proj/out"
    cp "$proj/out/$asm.dll" "$LIB/$asm.dll"
    echo "  $asm.dll  <-  reference-only build"
}

unity_package() { # package id, version, assembly name (= its .asmdef name)
    local pkg="$1" version="$2" asm="$3" dir="$REFS/build/_src/$1"
    fetch "https://download.packages.unity.com/$pkg/-/$pkg-$version.tgz" "$DL/$pkg-$version.tgz"
    mkdir -p "$dir"
    tar -xzf "$DL/$pkg-$version.tgz" -C "$dir"
    local asmdef
    asmdef="$(find "$dir" -name "$asm.asmdef" | head -n 1 || true)"
    [ -n "$asmdef" ] || { echo "error: $asm.asmdef not found in $pkg $version"; find "$dir" -name '*.asmdef'; exit 1; }
    ref_assembly "$asm" "$(dirname "$asmdef")"
}

echo "== Build helper"
dotnet build "$CI/tools/ExtractResources/ExtractResources.csproj" -c Release --nologo -v quiet -o "$REFS/build/_tools/ExtractResources"

echo "== Published mod package ($TS_NAMESPACE/$TS_NAME ${TS_VERSION:-latest})"
thunderstore "$TS_NAMESPACE" "$TS_NAME" "$TS_VERSION" "$REFS/package"
(cd "$REFS/package" && find . -type f -printf '  %10s  %p\n' | sort -k2)

echo "== Libraries shipped with the mod"
for f in Unity.XR.OpenXR.dll Unity.XR.Management.dll Unity.XR.CoreUtils.dll Unity.InputSystem.dll; do
    from_packages "$f" || { echo "error: $f is not in the published package"; exit 1; }
done

echo "== Haptics SDK libraries"
if ! from_packages Bhaptics.Tact.dll || ! from_packages ShockwaveManager.dll; then
    # Neither published package ships them as files. DrBibop's original patcher carries them as
    # embedded files (it used to copy them into the game folder), so they are taken out of it.
    echo "  not shipped as files; extracting them from DrBibop's original VRPatcher.dll"
    thunderstore DrBibop VRMod "" "$REFS/original"
    dotnet "$REFS/build/_tools/ExtractResources/ExtractResources.dll" "$REFS/original/patchers/VRPatcher.dll" "$REFS/original/embedded"
    for f in Bhaptics.Tact.dll ShockwaveManager.dll; do
        hit="$(find "$REFS/original/embedded" -type f -iname "*$f" | head -n 1 || true)"
        [ -n "$hit" ] || { echo "error: $f is not embedded in the original VRPatcher.dll"; exit 1; }
        cp "$hit" "$LIB/$f"
        echo "  $f  <-  ${hit#$REFS/}"
    done
fi

echo "== Reference-only builds of game-side assemblies"
unity_package com.unity.postprocessing "$POSTPROCESSING_VERSION" Unity.Postprocessing.Runtime
unity_package com.unity.xr.legacyinputhelpers "$LEGACY_INPUT_HELPERS_VERSION" UnityEngine.SpatialTracking
ref_assembly Decalicious "$CI/stubs/Decalicious"

echo "== refs/lib"
ls -l "$LIB"
