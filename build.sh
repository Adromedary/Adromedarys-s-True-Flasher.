#!/usr/bin/env bash
# ==========================================================================
#  True Flasher for Linux - build script            Made by Adromedary
#
#    ./build.sh            single self-contained binary  -> bin/linux-x64/trueflasher
#    ./build.sh arm64      same for 64-bit ARM (Raspberry Pi 4/5 etc.) -> bin/linux-arm64/
#    ./build.sh test       also build and run the simulated fake-drive tests
#
#  Needs the .NET 8 SDK (e.g. `sudo apt install dotnet-sdk-8.0`).
#  Falls back to Mono (`mono-mcs`) if .NET is not installed; the result then needs mono to run.
# ==========================================================================
set -euo pipefail
cd "$(dirname "$0")"

ARCH="x64"; RUN_TESTS=0
for a in "$@"; do
  case "$a" in
    arm64|aarch64) ARCH="arm64" ;;
    x64|amd64)     ARCH="x64" ;;
    test)          RUN_TESTS=1 ;;
    *) echo "unknown option: $a"; exit 2 ;;
  esac
done

if command -v dotnet >/dev/null 2>&1; then
  RID="linux-$ARCH"
  echo "Building True Flasher for $RID (self-contained single file)..."
  dotnet publish TrueFlasher.Linux.csproj -c Release -r "$RID" --self-contained true \
      -p:PublishSingleFile=true -p:PublishTrimmed=true -p:DebugType=none -o "bin/$RID" --nologo -v quiet
  echo "  OK  bin/$RID/trueflasher"
  if [ "$RUN_TESTS" = 1 ]; then
    echo; echo "Running simulation tests (needs about 2 GB of free RAM)..."; echo
    dotnet run --project tests/SimTests.csproj -c Release --nologo
  fi
  echo; echo "Run it with:  sudo bin/$RID/trueflasher"
elif command -v mcs >/dev/null 2>&1; then
  echo ".NET SDK not found - building with Mono instead..."
  mkdir -p bin/mono
  mcs -langversion:5 -optimize+ -out:bin/mono/trueflasher.exe \
      src/Pattern.cs src/Device.cs src/Engine.cs src/Report.cs src/linux/*.cs
  printf '#!/bin/sh\nexec mono "$(dirname "$0")/trueflasher.exe" "$@"\n' > bin/mono/trueflasher
  chmod +x bin/mono/trueflasher
  echo "  OK  bin/mono/trueflasher   (requires mono at run time)"
  if [ "$RUN_TESTS" = 1 ]; then
    mcs -langversion:5 -optimize+ -out:bin/mono/SimTests.exe tests/SimTests.cs src/Pattern.cs src/Device.cs src/Engine.cs src/Report.cs
    mono bin/mono/SimTests.exe
  fi
  echo; echo "Run it with:  sudo bin/mono/trueflasher"
else
  echo "Install the .NET 8 SDK (recommended) or mono-mcs, then run ./build.sh again."
  exit 1
fi
