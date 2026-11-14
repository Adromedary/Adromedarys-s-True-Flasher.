#!/usr/bin/env bash
# Build a True Flasher AppImage (x86_64 or aarch64, same as host).
# Needs: ./build.sh deps (dotnet-sdk-8.0) + appimagetool in PATH.
# Output: dist/TrueFlasher-1.0.0-<arch>.AppImage
set -euo pipefail
cd "$(dirname "$0")/../.."

VERSION="${VERSION:-1.0.0}"
ARCH="$(uname -m)"
case "$ARCH" in
  x86_64) RID="linux-x64" APPARCH="x86_64" ;;
  aarch64|arm64) RID="linux-arm64" APPARCH="aarch64" ;;
  *) echo "unsupported arch: $ARCH"; exit 1 ;;
esac

if [ ! -x "bin/$RID/trueflasher" ]; then
  if [ "$RID" = "linux-arm64" ]; then ./build.sh arm64; else ./build.sh; fi
fi

rm -rf AppDir dist
mkdir -p AppDir/usr/bin AppDir/usr/share/man/man1 dist

cp "bin/$RID/trueflasher" AppDir/usr/bin/trueflasher
chmod 755 AppDir/usr/bin/trueflasher
cp packaging/trueflasher.desktop AppDir/trueflasher.desktop
cp assets/icon_256.png AppDir/trueflasher.png
cp packaging/trueflasher.1 AppDir/usr/share/man/man1/
ln -sf usr/bin/trueflasher AppDir/AppRun

# appimagetool embeds update info when ARCH env is set; keep runtime-compat for older glibc
ARCH="$APPARCH" VERSION="$VERSION" appimagetool AppDir "dist/TrueFlasher-${VERSION}-${APPARCH}.AppImage"
echo "OK dist/TrueFlasher-${VERSION}-${APPARCH}.AppImage"
