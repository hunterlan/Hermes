#!/usr/bin/env bash
# Publishes Hermes for Linux and builds .deb and .rpm packages with nfpm.
# Usage: build-packages.sh <rid: linux-x64|linux-arm64> <version> <output-dir>
set -euo pipefail

rid="${1:?runtime identifier}"
version="${2:?version}"
out="$(mkdir -p "${3:?output directory}" && cd "$3" && pwd)"

root="$(cd "$(dirname "$0")/../.." && pwd)"
case "$rid" in
  linux-x64) arch=amd64 rpm_arch=x86_64 ;;
  linux-arm64) arch=arm64 rpm_arch=aarch64 ;;
  *) echo "unsupported runtime: $rid" >&2; exit 1 ;;
esac

publish="$(mktemp -d)"
trap 'rm -rf "$publish"' EXIT
dotnet publish "$root/src/Hermes.App" -c Release -r "$rid" -p:Version="$version" -o "$publish"

cd "$root"
export VERSION="$version" ARCH="$arch" PUBLISH_DIR="$publish"
nfpm package --config packaging/linux/nfpm.yaml --packager deb --target "$out/hermes_${version}_${arch}.deb"
nfpm package --config packaging/linux/nfpm.yaml --packager rpm --target "$out/hermes-${version}.${rpm_arch}.rpm"
