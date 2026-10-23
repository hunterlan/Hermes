#!/usr/bin/env bash
# Renders packaging/icons/hermes.svg into the committed icon files:
#   packaging/icons/png/hermes-<size>.png   Linux packages
#   packaging/icons/hermes.icns             macOS app bundle
#   src/Hermes.App/Assets/hermes.ico        Windows exe, installer and window icon
# Runs on macOS with only built-in tools (swift, iconutil).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

mkdir -p "$here/png"
swift "$here/render-icons.swift" "$here/hermes.svg" "$here/png" "$root/src/Hermes.App/Assets/hermes.ico"

iconset="$work/hermes.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  cp "$here/png/hermes-$size.png" "$iconset/icon_${size}x${size}.png"
  cp "$here/png/hermes-$((size * 2)).png" "$iconset/icon_${size}x${size}@2x.png"
done
iconutil -c icns "$iconset" -o "$here/hermes.icns"
