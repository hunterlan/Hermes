#!/usr/bin/env bash
# Publishes Hermes for macOS, wraps it in Hermes.app and packs a .dmg.
# Usage: build-dmg.sh <rid: osx-arm64|osx-x64> <version> <output-dir>
set -euo pipefail

rid="${1:?runtime identifier}"
version="${2:?version}"
out="${3:?output directory}"

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# An .app bundle carries its files anyway, so skip single-file extraction at startup.
dotnet publish "$root/src/Hermes.App" -c Release -r "$rid" -p:Version="$version" \
  -p:PublishSingleFile=false -o "$work/publish"

app="$work/dmg/Hermes.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$work/publish/." "$app/Contents/MacOS/"
sed "s/@VERSION@/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"
cp "$root/packaging/icons/hermes.icns" "$app/Contents/Resources/Hermes.icns"
cp "$root/LICENSE" "$app/Contents/Resources/LICENSE"

# Ad-hoc signature: Apple Silicon refuses to run unsigned code. Not notarized.
codesign --force --deep --sign - "$app"

ln -s /Applications "$work/dmg/Applications"
mkdir -p "$out"
hdiutil create -volname Hermes -srcfolder "$work/dmg" -ov -format UDZO \
  "$out/Hermes-$version-$rid.dmg"
