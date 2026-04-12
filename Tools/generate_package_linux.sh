#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

PACKAGE_BUILD_CONFIGURATION="${PACKAGE_BUILD_CONFIGURATION:-Release}"
PACKAGE_PLATFORM="${PACKAGE_PLATFORM:-x64}"
LOADER_AVALONIA_LINUX_RUNTIME="${LOADER_AVALONIA_LINUX_RUNTIME:-linux-x64}"
LOADER_AVALONIA_LINUX_PUBLISH_DIR="${LOADER_AVALONIA_LINUX_PUBLISH_DIR:-intermediate/publish/canonical/Loader.Avalonia}"

OUTPUT_ROOT=""
for candidate in \
  build/Source/Server \
  intermediate/build/Source/Server \
  intermediate/make/Source/Server \
  bin/x64_release; do
  if [ -d "$candidate" ]; then
    OUTPUT_ROOT="$candidate"
    break
  fi
 done

if [ -z "$OUTPUT_ROOT" ]; then
  echo "ERROR: No native Linux build output directory found. Check your build output path."
  exit 1
fi

if [ "${SKIP_DOTNET_PUBLISH:-0}" != "1" ]; then
  echo "Publishing Loader.Avalonia artifacts with canonical package-script settings..."
  dotnet publish Source/Loader.Avalonia/Loader.Avalonia.csproj \
    --configuration "$PACKAGE_BUILD_CONFIGURATION" \
    --runtime "$LOADER_AVALONIA_LINUX_RUNTIME" \
    --self-contained true \
    -p:Platform="$PACKAGE_PLATFORM" \
    -p:UseAppHost=true \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "$LOADER_AVALONIA_LINUX_PUBLISH_DIR"
fi

echo "Using package source output path: $OUTPUT_ROOT"
echo "Packaging into: $(pwd)/rekindled-server"

copy_with_log() {
  echo "Including: $1 -> $2"
  cp "$1" "$2"
}

copy_dir_with_log() {
  echo "Including directory: $1 -> $2"
  cp -R "$1" "$2"
}

rm -rf rekindled-server
mkdir -p rekindled-server
mkdir -p rekindled-server/Server
mkdir -p rekindled-server/Loader
mkdir -p rekindled-server/Loader.Avalonia
copy_with_log Resources/ReadMe.txt rekindled-server/ReadMe.txt

ERR=0

if [ -f "$OUTPUT_ROOT/steam_appid.txt" ]; then
  copy_with_log "$OUTPUT_ROOT/steam_appid.txt" rekindled-server/Server/
elif [ -f "Resources/steam_appid.txt" ]; then
  copy_with_log "Resources/steam_appid.txt" rekindled-server/Server/
else
  echo "WARNING: steam_appid.txt not found in $OUTPUT_ROOT or Resources"
  ERR=1
fi

if [ -f "$OUTPUT_ROOT/libsteam_api.so" ]; then
  copy_with_log "$OUTPUT_ROOT/libsteam_api.so" rekindled-server/Server/
elif [ -f "Source/ThirdParty/steam/redistributable_bin/linux64/libsteam_api.so" ]; then
  copy_with_log "Source/ThirdParty/steam/redistributable_bin/linux64/libsteam_api.so" rekindled-server/Server/
elif [ -f "Source/ThirdParty/steam/redistributable_bin/linux32/libsteam_api.so" ]; then
  copy_with_log "Source/ThirdParty/steam/redistributable_bin/linux32/libsteam_api.so" rekindled-server/Server/
else
  echo "WARNING: libsteam_api.so not found in $OUTPUT_ROOT or Source/ThirdParty"
  ERR=1
fi

if [ -f "$OUTPUT_ROOT/Server" ]; then
  copy_with_log "$OUTPUT_ROOT/Server" rekindled-server/Server/
elif [ -f "build/Source/Server/Server" ]; then
  copy_with_log "build/Source/Server/Server" rekindled-server/Server/
elif [ -f "intermediate/build/Source/Server/Server" ]; then
  copy_with_log "intermediate/build/Source/Server/Server" rekindled-server/Server/
elif [ -f "intermediate/make/Source/Server/Server" ]; then
  copy_with_log "intermediate/make/Source/Server/Server" rekindled-server/Server/
else
  echo "WARNING: Linux Server executable not found in $OUTPUT_ROOT or build/Source/Server or intermediate/make/Source/Server"
  ERR=1
fi

if [ -d "$OUTPUT_ROOT/WebUI" ]; then
  copy_dir_with_log "$OUTPUT_ROOT/WebUI/" rekindled-server/Server/WebUI/
elif [ -d "Source/WebUI" ]; then
  copy_dir_with_log "Source/WebUI/" rekindled-server/Server/WebUI/
else
  echo "WARNING: WebUI folder not found in $OUTPUT_ROOT or Source/WebUI"
  ERR=1
fi

mkdir -p rekindled-server/Loader
mkdir -p rekindled-server/Loader.Avalonia

if [ -d "$LOADER_AVALONIA_LINUX_PUBLISH_DIR" ] && [ -x "$LOADER_AVALONIA_LINUX_PUBLISH_DIR/Loader.Avalonia" ]; then
  copy_dir_with_log "$LOADER_AVALONIA_LINUX_PUBLISH_DIR/" rekindled-server/Loader.Avalonia/
else
  echo "WARNING: Loader.Avalonia output not found in canonical publish dir: $LOADER_AVALONIA_LINUX_PUBLISH_DIR"
  ERR=1
fi

if [ "$ERR" -eq 1 ]; then
  echo "ERROR: One or more required files were missing. Check build output path and CMake configuration."
  ls -la "$OUTPUT_ROOT"
  exit 1
fi

echo "Packaged files being added to the zip:"
find rekindled-server -type f | sort | sed 's#^#  #'
